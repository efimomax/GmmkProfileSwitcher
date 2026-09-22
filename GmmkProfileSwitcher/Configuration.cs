using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace GmmkProfileSwitcher
{
    /// <summary>
    /// Represents the user settings that will be saved to disk.
    /// Представляет пользовательские настройки, которые будут сохранены на диск.
    /// </summary>
    [DataContract]
    public class AppSettings
    {
        /// <summary>
        /// True if the app should start with Windows automatically.
        /// True, если приложение должно запускаться вместе с Windows автоматически.
        /// </summary>
        [DataMember]
        public bool StartWithWindows { get; set; } = false;

        /// <summary>
        /// True if the app should start minimized to the system tray without showing the settings window.
        /// True, если приложение должно запускаться свернутым в системный трей без показа окна настроек.
        /// </summary>
        [DataMember]
        public bool StartMinimized { get; set; } = false;

        /// <summary>
        /// True if diagnostic messages should be written to a log file next to the settings.
        /// True, если диагностические сообщения нужно писать в лог-файл рядом с настройками.
        /// </summary>
        [DataMember]
        public bool EnableLogging { get; set; } = false;

        /// <summary>
        /// Maps Language ID (LangID) to Profile Number (1, 2, 3).
        /// Сопоставляет ID языка (LangID) с номером профиля (1, 2, 3).
        /// </summary>
        [DataMember]
        public Dictionary<int, int> LanguageToProfileMap { get; set; } = new Dictionary<int, int>();
    }

    /// <summary>
    /// Manages loading and saving application configuration in JSON format.
    /// Управляет загрузкой и сохранением конфигурации приложения в формате JSON.
    /// </summary>
    public static class Configuration
    {
        // Path to the configuration folder in the user's AppData directory
        // Путь к папке с конфигурацией в директории AppData пользователя
        private static readonly string ConfigDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GmmkProfileSwitcher");

        /// <summary>
        /// Folder where settings and log files are stored.
        /// Папка, в которой хранятся настройки и файлы логов.
        /// </summary>
        public static string DataDirectory => ConfigDirectory;
        
        // Full path to the JSON settings file
        // Полный путь к файлу настроек JSON
        private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "settings.json");

        /// <summary>
        /// The currently loaded application settings.
        /// Текущие загруженные настройки приложения.
        /// </summary>
        public static AppSettings Current { get; private set; }

        static Configuration()
        {
            Current = new AppSettings();
        }

        /// <summary>
        /// Loads the settings from the JSON file on disk.
        /// Загружает настройки из JSON файла на диске.
        /// </summary>
        public static void Load()
        {
            GmmkProfileSwitcherLib.Logger.Initialize(ConfigDirectory);

            if (File.Exists(ConfigFilePath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(AppSettings), new DataContractJsonSerializerSettings
                        {
                            UseSimpleDictionaryFormat = true // Save dictionary as clean key-value pairs / Сохранять словарь как чистые пары ключ-значение
                        });
                        
                        var loaded = serializer.ReadObject(ms) as AppSettings;
                        if (loaded != null)
                        {
                            Current = loaded;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load config (Ошибка загрузки конфига): {ex.Message}");
                }
            }

            // Apply the logging switch as soon as settings are known.
            // Применяем переключатель логирования сразу, как только настройки известны.
            ApplyLoggingSetting();
        }

        /// <summary>
        /// Syncs the logger state with the current configuration.
        /// Синхронизирует состояние логгера с текущей конфигурацией.
        /// </summary>
        public static void ApplyLoggingSetting()
        {
            GmmkProfileSwitcherLib.Logger.IsEnabled = Current.EnableLogging;
        }

        /// <summary>
        /// Saves the current settings to the JSON file on disk.
        /// Сохраняет текущие настройки в JSON файл на диске.
        /// </summary>
        public static void Save()
        {
            try
            {
                // Create directory if it doesn't exist / Создаем директорию, если она не существует
                if (!Directory.Exists(ConfigDirectory))
                {
                    Directory.CreateDirectory(ConfigDirectory);
                }

                // Serialize object to JSON string / Сериализуем объект в JSON строку
                using (var ms = new MemoryStream())
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppSettings), new DataContractJsonSerializerSettings
                    {
                        UseSimpleDictionaryFormat = true
                    });
                    serializer.WriteObject(ms, Current);
                    File.WriteAllText(ConfigFilePath, Encoding.UTF8.GetString(ms.ToArray()));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save config (Ошибка сохранения конфига): {ex.Message}");
            }
        }
    }
}
