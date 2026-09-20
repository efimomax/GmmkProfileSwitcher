using System;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace GmmkProfileSwitcher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// Логика взаимодействия для App.xaml
    /// </summary>
    public partial class App : Application
    {
        private NotifyIcon _notifyIcon; // System tray icon / Иконка в системном трее
        private LayoutMonitor _layoutMonitor; // Background monitor for language changes / Фоновый монитор для смены языка
        private MainWindow _mainWindow; // The settings window / Окно настроек
        private System.Threading.Mutex _instanceMutex; // To prevent multiple instances / Для предотвращения запуска нескольких копий

        /// <summary>
        /// Triggered when the application starts.
        /// Вызывается при запуске приложения.
        /// </summary>
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Check if another instance is already running
            // Проверка, запущена ли уже другая копия приложения
            bool createdNew;
            _instanceMutex = new System.Threading.Mutex(true, "GmmkProfileSwitcher_SingleInstance_Mutex", out createdNew);
            if (!createdNew)
            {
                // Already running, exit silently / Уже запущено, тихо выходим
                Shutdown();
                return;
            }

            // Load configuration from file
            // Загружаем конфигурацию из файла
            Configuration.Load();

            // Initialize NotifyIcon (System Tray)
            // Инициализация иконки для системного трея (возле часов)
            _notifyIcon = new NotifyIcon();
            
            try 
            {
                var streamInfo = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
                if (streamInfo != null)
                {
                    _notifyIcon.Icon = new System.Drawing.Icon(streamInfo.Stream);
                }
                else
                {
                    _notifyIcon.Icon = System.Drawing.SystemIcons.Information;
                }
            }
            catch 
            {
                _notifyIcon.Icon = System.Drawing.SystemIcons.Information; // Fallback
            }
            
            _notifyIcon.Visible = true;
            _notifyIcon.Text = "GMMK Profile Switcher";
            
            // Create Context Menu for the tray icon
            // Создание контекстного меню для иконки в трее
            var contextMenu = new ContextMenu();
            contextMenu.MenuItems.Add("Settings", ShowSettings_Click);
            contextMenu.MenuItems.Add("-"); // Separator / Разделитель
            contextMenu.MenuItems.Add("Exit", Exit_Click);
            _notifyIcon.ContextMenu = contextMenu;

            // Open settings on double click
            // Открывать окно настроек по двойному клику
            _notifyIcon.DoubleClick += ShowSettings_Click;

            // Start layout monitoring in the background
            // Запуск мониторинга раскладки в фоновом режиме
            _layoutMonitor = new LayoutMonitor();
            _layoutMonitor.Start();

            // Show settings window if "Start Minimized" is NOT checked
            // Показывать окно настроек, если галочка "Запускать свернутым" НЕ стоит
            if (!Configuration.Current.StartMinimized)
            {
                ShowSettings();
            }
        }

        /// <summary>
        /// Event handler for the "Settings" menu item.
        /// Обработчик события для пункта меню "Настройки".
        /// </summary>
        private void ShowSettings_Click(object sender, EventArgs e)
        {
            ShowSettings();
        }

        /// <summary>
        /// Shows or activates the settings window.
        /// Показывает или активирует окно настроек.
        /// </summary>
        private void ShowSettings()
        {
            // If window doesn't exist, create it
            // Если окно не существует, создаем его
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
                _mainWindow.Show();
            }
            else
            {
                // Restore window if hidden or minimized and bring to front
                // Разворачиваем окно, если оно скрыто или свернуто, и выводим на передний план
                _mainWindow.Show();
                if (_mainWindow.WindowState == WindowState.Minimized)
                    _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            }
        }

        public static bool IsShuttingDown { get; private set; } = false;

        /// <summary>
        /// Event handler for the "Exit" menu item.
        /// Обработчик события для пункта меню "Выход".
        /// </summary>
        private void Exit_Click(object sender, EventArgs e)
        {
            IsShuttingDown = true;
            // Gracefully shutdown the WPF application
            // Корректно завершаем работу WPF-приложения
            Shutdown();
        }

        /// <summary>
        /// Triggered when the application is exiting.
        /// Вызывается при закрытии приложения.
        /// </summary>
        private void Application_Exit(object sender, ExitEventArgs e)
        {
            // Stop background monitoring and free resources
            // Останавливаем фоновый мониторинг и освобождаем ресурсы
            _layoutMonitor?.Stop();
            _layoutMonitor?.Dispose();

            // Hide and dispose the tray icon
            // Скрываем и удаляем иконку из трея
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
        }
    }
}
