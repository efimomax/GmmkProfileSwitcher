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

        /// <summary>
        /// Triggered when the application starts.
        /// Вызывается при запуске приложения.
        /// </summary>
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Load configuration from file
            // Загружаем конфигурацию из файла
            Configuration.Load();

            // Initialize NotifyIcon (System Tray)
            // Инициализация иконки для системного трея (возле часов)
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = System.Drawing.SystemIcons.Information; // Fallback icon / Иконка по умолчанию
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
            // If window doesn't exist, create and show it
            // Если окно не существует, создаем и показываем его
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
                _mainWindow.Closed += (s, args) => _mainWindow = null; // Clean up on close / Очистка при закрытии
                _mainWindow.Show();
            }
            else
            {
                // Restore window if minimized and bring to front
                // Разворачиваем окно, если оно свернуто, и выводим на передний план
                if (_mainWindow.WindowState == WindowState.Minimized)
                    _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            }
        }

        /// <summary>
        /// Event handler for the "Exit" menu item.
        /// Обработчик события для пункта меню "Выход".
        /// </summary>
        private void Exit_Click(object sender, EventArgs e)
        {
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
