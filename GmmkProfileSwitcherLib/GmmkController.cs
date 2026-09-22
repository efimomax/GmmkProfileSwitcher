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

        // Expected HID output report payload size for GMMK 1.
        // Ожидаемый размер полезной нагрузки HID output report для GMMK 1.
        private const int PacketSize = 64;

        // Delay between packets, giving the keyboard time to process each one.
        // Задержка между пакетами, чтобы клавиатура успевала обработать каждый.
        private const int PacketDelayMs = 30;

        // Initialization packets / Пакеты инициализации
        private static readonly byte[] p04_2f = BuildPacket("04 2f 00 03 2c");
        private static readonly byte[] p04_01 = BuildPacket("04 01 00 01");
        private static readonly byte[] p04_3d = BuildPacket("04 3d 00 05 38");
        private static readonly byte[] p04_67 = BuildPacket("04 67 00 05 38 2a");
        private static readonly byte[] p04_91 = BuildPacket("04 91 00 05 38 54");
        private static readonly byte[] p04_02 = BuildPacket("04 02 00 02");

        private static readonly byte[] p04_91_data = BuildPacket(
            "04 91 00 05 38 54 00 00 06 04 02 00 00 00 ff ff 00 00 00 00 00 00 00 03 00 00 00 00 00 00 00 00 " +
            "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 05 a2 a2 02 02 29 02 02 3a 02 02 3b");

        private static readonly Dictionary<int, byte[]> ProfileConfigPackets = new Dictionary<int, byte[]>
        {
            { 1, BuildPacket("04 3d 02 04 2c 00 00 00 06 04 00 00 00 00 ff ff 00 00 00 00 00 00 00 03") },
            { 2, BuildPacket("04 33 03 04 2c 00 00 00 06 04 00 00 00 ff fb f0 00 00 01 00 00 00 00 03 00 00 00 01 00 00 00 00 " +
                             "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 06 04") },
            { 3, BuildPacket("04 3f 02 04 2c 00 00 00 06 04 00 00 00 00 ff ff 00 00 02 00 00 00 00 03") }
        };

        /// <summary>
        /// Parses a hex string into a fixed-size packet, validating that it is not too long.
        /// Разбирает hex-строку в пакет фиксированного размера, проверяя, что она не слишком длинная.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if the packet exceeds <see cref="PacketSize"/>.</exception>
        private static byte[] BuildPacket(string hexString)
        {
            var bytes = hexString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(b => Convert.ToByte(b, 16))
                                 .ToArray();

            if (bytes.Length > PacketSize)
            {
                throw new ArgumentException(
                    $"Packet is {bytes.Length} bytes, which exceeds the {PacketSize}-byte report size: {hexString}",
                    nameof(hexString));
            }

            // Trailing zeros are implicit: the array is already zero-filled.
            // Хвостовые нули подразумеваются: массив уже заполнен нулями.
            var packet = new byte[PacketSize];
            Array.Copy(bytes, packet, bytes.Length);
            return packet;
        }

        /// <summary>
        /// Returns a list of found compatible GMMK keyboards.
        /// Возвращает список найденных совместимых клавиатур GMMK.
        /// </summary>
        /// <returns>List of <see cref="GmmkKeyboardDevice"/>.</returns>
        public static List<GmmkKeyboardDevice> SearchKeyboards()
        {
            try
            {
                // Enumerate devices fresh on every call. Any cached HidDevice becomes stale after
                // sleep/resume or USB re-plug, which makes every write fail silently.
                // Перечисляем устройства заново при каждом вызове. Любой закэшированный HidDevice
                // устаревает после сна/пробуждения или переподключения USB, из-за чего запись молча падает.
                var devices = DeviceList.Local.GetHidDevices(VendorId, ProductId)
                    .Where(d => d.GetMaxOutputReportLength() >= PacketSize)
                    .Select(d => new GmmkKeyboardDevice { Device = d })
                    .ToList();

                Logger.Log($"SearchKeyboards: found {devices.Count} compatible device(s)");
                return devices;
            }
            catch (Exception ex)
            {
                Logger.Log($"SearchKeyboards failed: {ex}");
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
            if (keyboard?.Device == null)
            {
                Logger.Log("SetProfile: no keyboard instance");
                return false;
            }

            if (!ProfileConfigPackets.TryGetValue(profileNumber, out var profilePacket))
            {
                Logger.Log($"SetProfile: unknown profile number {profileNumber}");
                return false;
            }

            // Setup initialization sequence based on profile
            // Настройка последовательности инициализации в зависимости от профиля
            var packetsToForceSend = new List<byte[]> { p04_2f, p04_01, p04_3d, p04_67 };

            if (profileNumber == 1 || profileNumber == 3)
            {
                packetsToForceSend.Add(p04_91);
            }

            packetsToForceSend.Add(p04_91_data);
            packetsToForceSend.Add(p04_02);
            packetsToForceSend.Add(p04_2f);

            // Add the specific profile config packet
            // Добавление пакета конфигурации конкретного профиля
            packetsToForceSend.Add(profilePacket);

            try
            {
                if (!keyboard.Device.TryOpen(out var stream))
                {
                    Logger.Log($"SetProfile({profileNumber}): TryOpen failed for {keyboard.DevicePath}");
                    return false;
                }

                using (stream)
                {
                    // Without a timeout a stale handle (e.g. after resume from sleep) can block forever.
                    // Без таймаута устаревший дескриптор (например, после выхода из сна) может зависнуть навсегда.
                    stream.WriteTimeout = 1000;

                    int reportLength = keyboard.Device.GetMaxOutputReportLength();

                    foreach (var packet in packetsToForceSend)
                    {
                        // The device may expect a report longer than our payload; pad if needed.
                        // Устройство может ожидать report длиннее нашей нагрузки; при необходимости дополняем.
                        byte[] toWrite = packet;
                        if (reportLength > packet.Length)
                        {
                            toWrite = new byte[reportLength];
                            Array.Copy(packet, toWrite, packet.Length);
                        }

                        stream.Write(toWrite);

                        // Short delay to ensure keyboard processes the packet
                        // Короткая задержка, чтобы клавиатура успела обработать пакет
                        Thread.Sleep(PacketDelayMs);
                    }
                }

                Logger.Log($"SetProfile({profileNumber}): sent {packetsToForceSend.Count} packets OK");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"SetProfile({profileNumber}) failed: {ex}");
            }

            return false;
        }
    }
}
