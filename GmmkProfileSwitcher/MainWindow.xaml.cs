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

        public MainWindow()
        {
            InitializeComponent();
            LoadSettingsToUI();
            this.DataContext = this; // Set DataContext for WPF binding / Устанавливаем DataContext для биндинга
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
        /// Triggered when the user clicks the "Save & Close" button.
        /// Вызывается при нажатии кнопки "Save & Close".
        /// </summary>
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            this.Close(); // Hides the window, but tray icon remains / Закрывает окно, но иконка в трее остается
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
