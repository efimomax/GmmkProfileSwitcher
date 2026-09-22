using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using GmmkProfileSwitcherLib;
using Microsoft.Win32;

namespace GmmkProfileSwitcher
{
    /// <summary>
    /// Monitors the active window's keyboard layout and switches GMMK profiles accordingly.
    /// Отслеживает раскладку клавиатуры активного окна и соответственно переключает профили GMMK.
    /// </summary>
    public class LayoutMonitor : IDisposable
    {
        #region WinAPI Imports
        
        // Gets the handle to the foreground window (the window with which the user is currently working).
        // Получает дескриптор активного окна (окна, с которым в данный момент работает пользователь).
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        // Retrieves the identifier of the thread that created the specified window.
        // Возвращает идентификатор потока, создавшего указанное окно.
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // Retrieves the active input locale identifier (keyboard layout) for the specified thread.
        // Получает идентификатор активной локали ввода (раскладки клавиатуры) для указанного потока.
        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);

        // Retrieves information about the active window or a specified GUI thread.
        // Получает информацию об активном окне или указанном GUI-потоке.
        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public int rcLeft, rcTop, rcRight, rcBottom;
        }

        // Sets an event hook function for a range of events.
        // Устанавливает функцию-перехватчик (хук) для диапазона событий.
        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        // Removes an event hook function created by a previous call to SetWinEventHook.
        // Удаляет функцию-перехватчик, созданную ранее вызовом SetWinEventHook.
        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003; // Event triggered when foreground window changes / Событие смены активного окна
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        #endregion

        private IntPtr _hookId = IntPtr.Zero; // Hook identifier / Идентификатор хука
        private WinEventDelegate _dele; // Delegate to prevent garbage collection / Делегат (для предотвращения сборки мусора)
        private IntPtr _lastHkl = IntPtr.Zero; // Last known keyboard layout / Последняя известная раскладка
        private int _lastAppliedProfile = 0; // Profile currently believed to be active / Профиль, который считается активным

        private GmmkKeyboardDevice _keyboard; // GMMK Keyboard instance / Экземпляр клавиатуры GMMK
        private CancellationTokenSource _cts; // Token for cancelling the polling loop / Токен для отмены цикла опроса

        // Guards CheckLayout: the WinEvent hook (UI thread) and the polling loop (pool thread)
        // can fire simultaneously and corrupt the HID write sequence.
        // Защищает CheckLayout: хук (UI-поток) и цикл опроса (поток пула) могут сработать
        // одновременно и нарушить последовательность HID-пакетов.
        private readonly object _checkLock = new object();

        // WinEvent hooks only fire on a thread that pumps messages, so install/remove must always
        // happen on the UI thread regardless of which thread requested it.
        // WinEvent-хуки работают только на потоке с насосом сообщений, поэтому установка/снятие
        // всегда должны выполняться на UI-потоке, независимо от того, кто их запросил.
        private readonly Dispatcher _dispatcher = Application.Current != null
            ? Application.Current.Dispatcher
            : Dispatcher.CurrentDispatcher;

        // Set by the hook to request an immediate re-check by the polling thread.
        // Устанавливается хуком, чтобы запросить немедленную перепроверку потоком опроса.
        private int _recheckRequested;

        // Prevents overlapping reinitializations (resume + unlock often fire together).
        // Предотвращает наложение реинициализаций (resume и unlock часто срабатывают вместе).
        private int _reinitInProgress;

        /// <summary>
        /// Raised when the monitor state changes, so the UI can display diagnostics.
        /// Вызывается при изменении состояния монитора, чтобы UI мог показать диагностику.
        /// </summary>
        public event EventHandler<string> StatusChanged;

        /// <summary>
        /// True if the system hook is currently installed.
        /// True, если системный хук установлен в данный момент.
        /// </summary>
        public bool IsHookInstalled => _hookId != IntPtr.Zero;

        /// <summary>
        /// True if a compatible keyboard is currently known to the monitor.
        /// True, если совместимая клавиатура сейчас известна монитору.
        /// </summary>
        public bool IsKeyboardConnected => _keyboard != null;

        public LayoutMonitor()
        {
            _dele = new WinEventDelegate(WinEventProc);
        }

        /// <summary>
        /// Installs the WinEvent hook on the UI thread (required for the hook to fire).
        /// Устанавливает WinEvent-хук на UI-потоке (обязательно, иначе хук не срабатывает).
        /// </summary>
        private void InstallHook()
        {
            if (_dispatcher.HasShutdownStarted) return;

            _dispatcher.Invoke(new Action(() =>
            {
                if (_hookId != IntPtr.Zero) return;
                _hookId = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _dele, 0, 0, WINEVENT_OUTOFCONTEXT);
            }));
        }

        /// <summary>
        /// Removes the WinEvent hook on the UI thread that created it.
        /// Снимает WinEvent-хук на том же UI-потоке, который его создал.
        /// </summary>
        private void RemoveHook()
        {
            if (_dispatcher.HasShutdownStarted)
            {
                // Process is going away; the OS releases the hook automatically.
                // Процесс завершается; ОС освободит хук автоматически.
                _hookId = IntPtr.Zero;
                return;
            }

            _dispatcher.Invoke(new Action(() =>
            {
                if (_hookId == IntPtr.Zero) return;
                UnhookWinEvent(_hookId);
                _hookId = IntPtr.Zero;
            }));
        }

        /// <summary>
        /// Starts the layout monitoring process.
        /// Запускает процесс мониторинга раскладки.
        /// </summary>
        public void Start()
        {
            FindKeyboard();

            // Set up a system hook to detect when the user switches to a different window
            // Устанавливаем системный хук для отслеживания переключения пользователя на другое окно
            InstallHook();

            // Start a background polling loop to detect layout changes (e.g. using Alt+Shift) within the same window
            // Запускаем фоновый цикл опроса для отслеживания смены раскладки (например, по Alt+Shift) внутри одного окна
            if (_cts == null)
            {
                _cts = new CancellationTokenSource();
                Task.Run(() => PollingLoop(_cts.Token));
            }

            // React to resume from sleep: hooks and HID handles are usually dead by then.
            // Реагируем на выход из сна: хуки и HID-дескрипторы к этому моменту обычно мертвы.
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;

            RaiseStatus("Monitoring started / Мониторинг запущен");
        }

        /// <summary>
        /// Stops the layout monitoring process and cleans up resources.
        /// Останавливает процесс мониторинга раскладки и освобождает ресурсы.
        /// </summary>
        public void Stop()
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;

            // Remove the system hook / Удаляем системный хук
            RemoveHook();

            // Stop the polling task / Останавливаем задачу опроса
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// Forces a full re-initialization: reinstalls the system hook, re-discovers the keyboard
        /// and re-applies the profile for the current layout.
        /// Принудительная полная реинициализация: переустанавливает системный хук, заново находит
        /// клавиатуру и повторно применяет профиль для текущей раскладки.
        /// </summary>
        public void Reinitialize()
        {
            // Resume + unlock can fire together; a second concurrent reinit is pointless.
            // Resume и unlock могут сработать одновременно; второй параллельный реинит бессмыслен.
            if (Interlocked.CompareExchange(ref _reinitInProgress, 1, 0) == 1) return;

            try
            {
                lock (_checkLock)
                {
                    // Drop the cached device so the next check performs a fresh HID enumeration.
                    // Сбрасываем кэш устройства, чтобы следующая проверка выполнила новое перечисление HID.
                    _keyboard = null;

                    // Forget the last state so the profile is re-sent even if the layout did not change.
                    // Забываем последнее состояние, чтобы профиль отправился заново, даже если раскладка не менялась.
                    _lastHkl = IntPtr.Zero;
                    _lastAppliedProfile = 0;
                }

                // Reinstall the hook on the UI thread / Переустанавливаем хук на UI-потоке
                RemoveHook();
                InstallHook();

                // Restart the polling loop if it died for any reason
                // Перезапускаем цикл опроса, если он по какой-то причине завершился
                if (_cts == null)
                {
                    _cts = new CancellationTokenSource();
                    Task.Run(() => PollingLoop(_cts.Token));
                }

                // Reinitialization is an explicit request, so wait for the lock instead of skipping.
                // Реинициализация — явный запрос, поэтому ждём блокировку, а не пропускаем.
                CheckLayout(waitForLock: true);

                RaiseStatus(_keyboard != null
                    ? "Reinitialized, keyboard found / Реинициализировано, клавиатура найдена"
                    : "Reinitialized, keyboard NOT found / Реинициализировано, клавиатура НЕ найдена");
            }
            finally
            {
                Interlocked.Exchange(ref _reinitInProgress, 0);
            }
        }

        /// <summary>
        /// Handles resume from sleep / hibernate.
        /// Обрабатывает выход из сна / гибернации.
        /// </summary>
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                Logger.Log("PowerMode: Resume -> scheduling reinitialization in 5s");

                // Give USB stack time to re-enumerate the keyboard before talking to it.
                // Даём USB-стеку время заново перечислить клавиатуру перед обращением к ней.
                Task.Delay(5000).ContinueWith(_ => Reinitialize());
            }
        }

        /// <summary>
        /// Handles unlock / session reconnect, which also invalidates hooks.
        /// Обрабатывает разблокировку / переподключение сеанса, что тоже ломает хуки.
        /// </summary>
        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock ||
                e.Reason == SessionSwitchReason.SessionLogon ||
                e.Reason == SessionSwitchReason.RemoteConnect)
            {
                Logger.Log($"SessionSwitch: {e.Reason} -> scheduling reinitialization in 2s");
                Task.Delay(2000).ContinueWith(_ => Reinitialize());
            }
        }

        private void RaiseStatus(string message)
        {
            Logger.Log(message);
            StatusChanged?.Invoke(this, message);
        }

        /// <summary>
        /// Attempts to find an attached GMMK keyboard.
        /// Пытается найти подключенную клавиатуру GMMK.
        /// </summary>
        private void FindKeyboard()
        {
            if (_keyboard == null)
            {
                var keyboards = GmmkController.SearchKeyboards();
                if (keyboards.Count > 0)
                {
                    _keyboard = keyboards[0];
                }
            }
        }

        /// <summary>
        /// Callback function executed when the foreground window changes.
        /// Функция обратного вызова, выполняемая при смене активного окна.
        /// </summary>
        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // Runs on the UI thread. Never touch HID here: SetProfile blocks for ~300ms and would
            // freeze the UI on every window switch. Just signal the polling loop.
            // Выполняется на UI-потоке. Не трогаем HID: SetProfile блокирует ~300мс и заморозил бы
            // интерфейс при каждом переключении окна. Просто сигнализируем циклу опроса.
            Interlocked.Exchange(ref _recheckRequested, 1);
        }

        /// <summary>
        /// Background loop that periodically checks the layout.
        /// Фоновый цикл, который периодически проверяет раскладку.
        /// </summary>
        private async Task PollingLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    CheckLayout();

                    // React to a foreground change within ~40ms instead of waiting a full 200ms tick.
                    // Реагируем на смену окна за ~40мс, не дожидаясь полного тика в 200мс.
                    for (int i = 0; i < 5; i++)
                    {
                        await Task.Delay(40, token);
                        if (Interlocked.Exchange(ref _recheckRequested, 0) == 1) break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown / Штатное завершение
            }
        }

        /// <summary>
        /// Resolves the keyboard layout of the thread that actually owns the input focus.
        /// Определяет раскладку потока, который реально владеет фокусом ввода.
        /// </summary>
        private IntPtr GetActiveLayout()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return IntPtr.Zero;

            uint threadId = GetWindowThreadProcessId(hwnd, out _);
            if (threadId == 0) return IntPtr.Zero;

            // For UWP/Store apps the foreground window belongs to ApplicationFrameHost, whose thread
            // reports a stale layout. GUITHREADINFO.hwndFocus points at the real input window.
            // У UWP/Store-приложений активное окно принадлежит ApplicationFrameHost, чей поток
            // отдаёт устаревшую раскладку. GUITHREADINFO.hwndFocus указывает на реальное окно ввода.
            var gui = new GUITHREADINFO();
            gui.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
            if (GetGUIThreadInfo(threadId, ref gui) && gui.hwndFocus != IntPtr.Zero)
            {
                uint focusThreadId = GetWindowThreadProcessId(gui.hwndFocus, out _);
                if (focusThreadId != 0) threadId = focusThreadId;
            }

            return GetKeyboardLayout(threadId);
        }

        /// <summary>
        /// Checks the current keyboard layout of the active window and updates the keyboard profile.
        /// Проверяет текущую раскладку клавиатуры активного окна и обновляет профиль клавиатуры.
        /// </summary>
        /// <param name="waitForLock">
        /// True to block until the lock is free (explicit user/system request), false to skip if busy (polling).
        /// True — ждать освобождения блокировки (явный запрос пользователя/системы), false — пропустить, если занято (опрос).
        /// </param>
        private void CheckLayout(bool waitForLock = false)
        {
            // Serialize access: a profile switch takes ~300ms, and the polling loop must not
            // overlap with an explicit reinitialization request.
            // Сериализуем доступ: переключение профиля занимает ~300мс, и цикл опроса не должен
            // накладываться на явный запрос реинициализации.
            if (!Monitor.TryEnter(_checkLock, waitForLock ? Timeout.Infinite : 0)) return;
            try
            {
                if (_keyboard == null)
                {
                    // Try to find it again if it was unplugged/replugged
                    // Пытаемся найти её снова, если она была отключена/переподключена
                    FindKeyboard();
                    if (_keyboard == null) return;
                }

                IntPtr hkl = GetActiveLayout();
                if (hkl == IntPtr.Zero) return;

                // Extract Language ID / Извлекаем ID языка
                int langId = (int)(hkl.ToInt64() & 0xFFFF);

                int targetProfile = 1; // Default fallback / Резервный профиль по умолчанию

                // Get configured profile from user settings / Получаем настроенный профиль из настроек пользователя
                if (Configuration.Current.LanguageToProfileMap.TryGetValue(langId, out int configuredProfile))
                {
                    targetProfile = configuredProfile;
                }

                // Nothing to do if the layout and the resulting profile are unchanged
                // Ничего не делаем, если раскладка и целевой профиль не изменились
                if (hkl == _lastHkl && targetProfile == _lastAppliedProfile) return;

                Logger.Log($"Layout change detected: hkl=0x{hkl.ToInt64():X} langId=0x{langId:X4} -> profile {targetProfile}");

                // Apply the profile to the physical keyboard / Применяем профиль к физической клавиатуре
                if (GmmkController.SetProfile(_keyboard, targetProfile))
                {
                    _lastHkl = hkl;
                    _lastAppliedProfile = targetProfile;
                }
                else
                {
                    // The write failed: the handle is stale (sleep/resume, re-plug).
                    // Drop the device and keep the old state so the next tick retries.
                    // Запись не удалась: дескриптор устарел (сон, переподключение).
                    // Сбрасываем устройство и сохраняем старое состояние, чтобы следующий тик повторил попытку.
                    Logger.Log("SetProfile failed, dropping cached device and retrying on next tick");
                    _keyboard = null;
                    _lastHkl = IntPtr.Zero;
                    _lastAppliedProfile = 0;
                }
            }
            finally
            {
                Monitor.Exit(_checkLock);
            }
        }

        public void Dispose()
        {
            Stop();
            StatusChanged = null;
        }
    }
}
