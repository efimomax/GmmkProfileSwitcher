using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GmmkProfileSwitcherLib;

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

        private GmmkKeyboardDevice _keyboard; // GMMK Keyboard instance / Экземпляр клавиатуры GMMK
        private CancellationTokenSource _cts; // Token for cancelling the polling loop / Токен для отмены цикла опроса

        public LayoutMonitor()
        {
            _dele = new WinEventDelegate(WinEventProc);
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
            if (_hookId == IntPtr.Zero)
            {
                _hookId = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _dele, 0, 0, WINEVENT_OUTOFCONTEXT);
            }

            // Start a background polling loop to detect layout changes (e.g. using Alt+Shift) within the same window
            // Запускаем фоновый цикл опроса для отслеживания смены раскладки (например, по Alt+Shift) внутри одного окна
            if (_cts == null)
            {
                _cts = new CancellationTokenSource();
                Task.Run(() => PollingLoop(_cts.Token));
            }
        }

        /// <summary>
        /// Stops the layout monitoring process and cleans up resources.
        /// Останавливает процесс мониторинга раскладки и освобождает ресурсы.
        /// </summary>
        public void Stop()
        {
            // Remove the system hook / Удаляем системный хук
            if (_hookId != IntPtr.Zero)
            {
                UnhookWinEvent(_hookId);
                _hookId = IntPtr.Zero;
            }

            // Stop the polling task / Останавливаем задачу опроса
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
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
            CheckLayout();
        }

        /// <summary>
        /// Background loop that periodically checks the layout.
        /// Фоновый цикл, который периодически проверяет раскладку.
        /// </summary>
        private async Task PollingLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                CheckLayout();
                await Task.Delay(200, token); // Poll every 200ms / Опрашиваем каждые 200мс
            }
        }

        /// <summary>
        /// Checks the current keyboard layout of the active window and updates the keyboard profile.
        /// Проверяет текущую раскладку клавиатуры активного окна и обновляет профиль клавиатуры.
        /// </summary>
        private void CheckLayout()
        {
            if (_keyboard == null)
            {
                // Try to find it again if it was unplugged/replugged
                // Пытаемся найти её снова, если она была отключена/переподключена
                FindKeyboard();
                if (_keyboard == null) return;
            }

            // Get the active window / Получаем активное окно
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            // Get the language (layout) of that window / Получаем язык (раскладку) этого окна
            uint threadId = GetWindowThreadProcessId(hwnd, out _);
            IntPtr hkl = GetKeyboardLayout(threadId);

            // If the layout has changed / Если раскладка изменилась
            if (hkl != _lastHkl && hkl != IntPtr.Zero)
            {
                _lastHkl = hkl;
                
                // Extract Language ID / Извлекаем ID языка
                int langId = (int)(hkl.ToInt64() & 0xFFFF);
                
                int targetProfile = 1; // Default fallback / Резервный профиль по умолчанию

                // Get configured profile from user settings / Получаем настроенный профиль из настроек пользователя
                if (Configuration.Current.LanguageToProfileMap.TryGetValue(langId, out int configuredProfile))
                {
                    targetProfile = configuredProfile;
                }

                // Apply the profile to the physical keyboard / Применяем профиль к физической клавиатуре
                GmmkController.SetProfile(_keyboard, targetProfile);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
