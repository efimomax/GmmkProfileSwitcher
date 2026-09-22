using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GmmkProfileSwitcher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml (Settings Window)
    /// Логика взаимодействия для MainWindow.xaml (Окно настроек)
    /// </summary>
    public partial class MainWindow : Window
    {
        // Collection for DataBinding to the UI list / Коллекция для привязки данных к списку в UI
        public ObservableCollection<LanguageItem> Languages { get; set; } = new ObservableCollection<LanguageItem>();

        // Reference to the background monitor, used for manual reinitialization
        // Ссылка на фоновый монитор, используется для ручной реинициализации
        private readonly LayoutMonitor _monitor;

        public MainWindow(LayoutMonitor monitor)
        {
            InitializeComponent();
            _monitor = monitor;
            LoadSettingsToUI();
            this.DataContext = this; // Set DataContext for WPF binding / Устанавливаем DataContext для биндинга

            if (_monitor != null)
            {
                _monitor.StatusChanged += OnMonitorStatusChanged;
            }
            UpdateStatusText();
        }

        /// <summary>
        /// Handles status notifications coming from the background monitor thread.
        /// Обрабатывает уведомления о состоянии, приходящие из фонового потока монитора.
        /// </summary>
        private void OnMonitorStatusChanged(object sender, string message)
        {
            Dispatcher.BeginInvoke(new Action(() => txtStatus.Text = message));
        }

        /// <summary>
        /// Refreshes the diagnostic status line.
        /// Обновляет строку диагностики.
        /// </summary>
        private void UpdateStatusText()
        {
            if (_monitor == null)
            {
                txtStatus.Text = "Monitor unavailable / Монитор недоступен";
                return;
            }

            txtStatus.Text = string.Format(
                "Hook: {0} | Keyboard: {1}",
                _monitor.IsHookInstalled ? "OK" : "NOT INSTALLED",
                _monitor.IsKeyboardConnected ? "found" : "not found");
        }

        /// <summary>
        /// Forces the monitor to reinstall the system hook and re-detect the keyboard.
        /// Принудительно заставляет монитор переустановить системный хук и заново найти клавиатуру.
        /// </summary>
        private void BtnReinit_Click(object sender, RoutedEventArgs e)
        {
            if (_monitor == null) return;

            btnReinit.IsEnabled = false;
            txtStatus.Text = "Reinitializing... / Реинициализация...";

            // Run off the UI thread: the HID write sequence blocks for a few hundred milliseconds.
            // Выполняем вне UI-потока: отправка HID-пакетов блокирует на несколько сотен миллисекунд.
            System.Threading.Tasks.Task.Run(() => _monitor.Reinitialize())
                .ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() =>
                {
                    btnReinit.IsEnabled = true;
                    UpdateStatusText();
                })));
        }

        /// <summary>
        /// Loads current settings and installed languages into the UI.
        /// Загружает текущие настройки и установленные языки в интерфейс.
        /// </summary>
        private void LoadSettingsToUI()
        {
            // Set checkboxes based on loaded configuration / Устанавливаем чекбоксы на основе загруженной конфигурации
            chkStartWithWindows.IsChecked = Configuration.Current.StartWithWindows;
            chkStartMinimized.IsChecked = Configuration.Current.StartMinimized;
            chkEnableLogging.IsChecked = Configuration.Current.EnableLogging;

            // Load installed languages from Windows OS / Получаем установленные языки из ОС Windows
            var installedLangs = InputLanguage.InstalledInputLanguages;
            
            int defaultProfileCounter = 1;

            foreach (InputLanguage lang in installedLangs)
            {
                // Get the unique Language ID / Получаем уникальный ID языка
                int langId = (int)lang.Handle.ToInt64() & 0xFFFF;
                
                // If it's in config, use it; otherwise assign default 1, 2, 3
                // Если язык есть в конфиге, используем его; иначе назначаем по умолчанию 1, 2, 3
                int profile = defaultProfileCounter;
                if (Configuration.Current.LanguageToProfileMap.TryGetValue(langId, out int configuredProfile))
                {
                    profile = configuredProfile;
                }
                else
                {
                    // For newly discovered layouts, assign ascending profiles up to 3, then stick to 1
                    // Для новых раскладок назначаем профили по возрастанию до 3, затем сбрасываем на 1
                    Configuration.Current.LanguageToProfileMap[langId] = profile;
                    defaultProfileCounter = defaultProfileCounter < 3 ? defaultProfileCounter + 1 : 1;
                }

                // Add to the list to display in UI / Добавляем в список для отображения в UI
                Languages.Add(new LanguageItem
                {
                    LangId = langId,
                    LanguageName = lang.Culture.DisplayName,
                    SelectedProfile = profile,
                    Parent = this
                });
            }

            // Bind the ListView to our collection / Привязываем ListView к нашей коллекции
            lvLanguages.ItemsSource = Languages;
        }

        /// <summary>
        /// Triggered when the user checks/unchecks the checkboxes.
        /// Вызывается, когда пользователь ставит/снимает галочки в чекбоксах.
        /// </summary>
        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            // Ignore events before window is fully loaded / Игнорируем события до полной загрузки окна
            if (!this.IsLoaded) return;

            // Update configuration object / Обновляем объект конфигурации
            Configuration.Current.StartWithWindows = chkStartWithWindows.IsChecked == true;
            Configuration.Current.StartMinimized = chkStartMinimized.IsChecked == true;
            Configuration.Current.EnableLogging = chkEnableLogging.IsChecked == true;

            // Turn the logger on/off right away / Сразу включаем/выключаем логгер
            Configuration.ApplyLoggingSetting();
            
            // Apply autorun changes to OS registry / Применяем изменения автозагрузки в реестр ОС
            UpdateAutorunRegistry(Configuration.Current.StartWithWindows);
            
            // Save settings to disk / Сохраняем настройки на диск
            Configuration.Save();
        }

        /// <summary>
        /// Updates the profile for a specific language in the configuration.
        /// Обновляет профиль для конкретного языка в конфигурации.
        /// </summary>
        public void ProfileChanged(int langId, int newProfile)
        {
            Configuration.Current.LanguageToProfileMap[langId] = newProfile;
            Configuration.Save();
        }

        /// <summary>
        /// Triggered when the user selects a new profile from the dropdown list.
        /// Вызывается, когда пользователь выбирает новый профиль из выпадающего списка.
        /// </summary>
        private void Profile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.ComboBox cb && cb.DataContext is LanguageItem item)
            {
                if (cb.SelectedItem is int profile)
                {
                    ProfileChanged(item.LangId, profile);
                }
            }
        }

        /// <summary>
        /// Adds or removes the application from the Windows autorun registry key.
        /// Добавляет или удаляет приложение из ключа реестра автозагрузки Windows.
        /// </summary>
        /// <param name="enable">True to enable autorun, False to disable / True - включить автозагрузку, False - отключить</param>
        private void UpdateAutorunRegistry(bool enable)
        {
            try
            {
                string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKey, true))
                {
                    if (enable)
                    {
                        // Add path to current executable with quotes / Записываем путь к текущему .exe файлу в кавычках
                        key.SetValue("GmmkProfileSwitcher", $"\"{System.Reflection.Assembly.GetExecutingAssembly().Location}\"");
                    }
                    else
                    {
                        // Remove the entry / Удаляем запись
                        key.DeleteValue("GmmkProfileSwitcher", false);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to update registry for autorun (Ошибка обновления реестра): {ex.Message}");
            }
        }

        /// <summary>
        /// Opens today's log file, or the containing folder if no log exists yet.
        /// Открывает сегодняшний файл лога, либо папку с логами, если файла ещё нет.
        /// </summary>
        private void BtnOpenLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logPath = GmmkProfileSwitcherLib.Logger.CurrentLogFilePath;

                if (!string.IsNullOrEmpty(logPath) && System.IO.File.Exists(logPath))
                {
                    System.Diagnostics.Process.Start(logPath);
                }
                else
                {
                    System.IO.Directory.CreateDirectory(Configuration.DataDirectory);
                    System.Diagnostics.Process.Start(Configuration.DataDirectory);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open log (Не удалось открыть лог): {ex.Message}");
            }
        }

        /// <summary>
        /// Triggered when the user clicks the "Save & Close" button.
        /// Вызывается при нажатии кнопки "Save & Close".
        /// </summary>
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            this.Hide(); // Hides the window to tray / Скрывает окно в трей
        }

        /// <summary>
        /// Triggered when the window is trying to close (e.g. by clicking 'X').
        /// Вызывается при попытке закрыть окно (например, нажатием на 'X').
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (App.IsShuttingDown) return; // Allow actual close if shutting down / Разрешаем закрытие, если программа завершается
            
            e.Cancel = true; // Prevent the window from actually destroying itself / Предотвращаем уничтожение окна
            this.Hide();     // Hide to tray instead / Вместо этого скрываем в трей
        }

        /// <summary>
        /// Refresh diagnostics every time the window becomes visible again.
        /// Обновляем диагностику каждый раз, когда окно снова становится видимым.
        /// </summary>
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            UpdateStatusText();
        }
    }

    /// <summary>
    /// Represents a single language setting row in the UI.
    /// Представляет одну строку настройки языка в интерфейсе.
    /// </summary>
    public class LanguageItem
    {
        public int LangId { get; set; } // The Language ID / ID языка
        public string LanguageName { get; set; } // The display name of the language / Отображаемое имя языка
        public int SelectedProfile { get; set; } // The assigned keyboard profile / Назначенный профиль клавиатуры
        public MainWindow Parent { get; set; } // Reference to main window / Ссылка на главное окно
    }
}
