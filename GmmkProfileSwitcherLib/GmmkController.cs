using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HidSharp;

namespace GmmkProfileSwitcherLib
{
    /// <summary>
    /// Represents a GMMK Keyboard device.
    /// Представляет устройство GMMK-клавиатуры.
    /// </summary>
    public class GmmkKeyboardDevice
    {
        public HidDevice Device { get; internal set; }
        public string DevicePath => Device?.DevicePath;
    }

    /// <summary>
    /// Controller for interacting with GMMK keyboards.
    /// Контроллер для взаимодействия с клавиатурами GMMK.
    /// </summary>
    public static class GmmkController
    {
        private const int VendorId = 0x0C45;
        private const int ProductId = 0x652F;

        // Initialization packets / Пакеты инициализации
        private static readonly string[] InitPackets = new[]
        {
            "04 2f 00 03 2c 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00", // p04_2f
            "04 01 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00", // p04_01
            "04 3d 00 05 38 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00", // p04_3d
            "04 67 00 05 38 2a 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00", // p04_67
            "04 91 00 05 38 54 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"  // p04_91
        };
        private static readonly string p04_02 = "04 02 00 02 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00";
        private static readonly string p04_91_data = "04 91 00 05 38 54 00 00 06 04 02 00 00 00 ff ff 00 00 00 00 00 00 00 03 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 05 a2 a2 02 02 29 02 02 3a 02 02 3b";

        private static readonly Dictionary<int, string> ProfileConfigPackets = new Dictionary<int, string>
        {
            { 1, "04 3d 02 04 2c 00 00 00 06 04 00 00 00 00 ff ff 00 00 00 00 00 00 00 03 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00" },
            { 2, "04 33 03 04 2c 00 00 00 06 04 00 00 00 ff fb f0 00 00 01 00 00 00 00 03 00 00 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 06 04 00 00 00 00 00 00 00 00 00 00 00 00" },
            { 3, "04 3f 02 04 2c 00 00 00 06 04 00 00 00 00 ff ff 00 00 02 00 00 00 00 03 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00" }
        };

        /// <summary>
        /// Returns a list of found compatible GMMK keyboards.
        /// Возвращает список найденных совместимых клавиатур GMMK.
        /// </summary>
        /// <returns>List of <see cref="GmmkKeyboardDevice"/>.</returns>
        public static List<GmmkKeyboardDevice> SearchKeyboards()
        {
            try
            {
                var devices = DeviceList.Local.GetHidDevices(VendorId, ProductId)
                    .Where(d => d.GetMaxOutputReportLength() >= 64)
                    .Select(d => new GmmkKeyboardDevice { Device = d })
                    .ToList();

                return devices;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error searching for keyboards: {ex.Message}");
                return new List<GmmkKeyboardDevice>();
            }
        }

        /// <summary>
        /// Switches the active profile on the specified keyboard.
        /// Переключает активный профиль на указанной клавиатуре.
        /// </summary>
        /// <param name="keyboard">The keyboard instance obtained from SearchKeyboards(). / Экземпляр клавиатуры.</param>
        /// <param name="profileNumber">Profile number (1, 2, or 3). / Номер профиля (от 1 до 3).</param>
        /// <returns>True if successful, otherwise false. / True в случае успеха, иначе false.</returns>
        public static bool SetProfile(GmmkKeyboardDevice keyboard, int profileNumber)
        {
            if (keyboard?.Device == null || !ProfileConfigPackets.ContainsKey(profileNumber))
                return false;

            var packetsToForceSend = new List<string>();

            // Setup initialization sequence based on profile
            // Настройка последовательности инициализации в зависимости от профиля
            packetsToForceSend.Add(InitPackets[0]); // 04_2f
            packetsToForceSend.Add(InitPackets[1]); // 04_01
            packetsToForceSend.Add(InitPackets[2]); // 04_3d
            packetsToForceSend.Add(InitPackets[3]); // 04_67

            if (profileNumber == 1 || profileNumber == 3)
            {
                packetsToForceSend.Add(InitPackets[4]); // 04_91
            }
            
            packetsToForceSend.Add(p04_91_data);
            packetsToForceSend.Add(p04_02);
            packetsToForceSend.Add(InitPackets[0]); // 04_2f
            
            // Add the specific profile config packet
            // Добавление пакета конфигурации конкретного профиля
            packetsToForceSend.Add(ProfileConfigPackets[profileNumber]);

            try
            {
                if (keyboard.Device.TryOpen(out var stream))
                {
                    using (stream)
                    {
                        int len = keyboard.Device.GetMaxOutputReportLength();
                        foreach (var hexString in packetsToForceSend)
                        {
                            byte[] packet = ParseHex(hexString, len);
                            stream.Write(packet);
                            // Short delay to ensure keyboard processes the packet
                            // Короткая задержка, чтобы клавиатура успела обработать пакет
                            Thread.Sleep(30); 
                        }
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sending profile to keyboard: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Converts a hex string representation into a byte array.
        /// Конвертирует строковое шестнадцатеричное представление в массив байтов.
        /// </summary>
        private static byte[] ParseHex(string hexString, int targetLength)
        {
            var bytes = hexString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(b => Convert.ToByte(b, 16))
                                 .ToArray();

            byte[] result = new byte[targetLength];
            Array.Copy(bytes, result, Math.Min(bytes.Length, targetLength));
            return result;
        }
    }
}