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
    /// <remarks>
    /// This is a thin wrapper over HidSharp's HidDevice rather than a bare HidDevice on purpose.
    /// The library is published as a reusable public API (the repository is public, so other
    /// projects may want to switch GMMK profiles from their own code), and the wrapper keeps
    /// HidSharp from leaking into the caller's code: consumers only ever pass values returned
    /// by SearchKeyboards() back into SetProfile(). It also leaves room to carry extra state
    /// later without breaking the signature of every public method.
    ///
    /// Это намеренно тонкая обёртка над HidDevice из HidSharp, а не сам HidDevice.
    /// Библиотека опубликована как переиспользуемый публичный API (репозиторий открытый, и
    /// кому-то может понадобиться переключать профили GMMK из собственного кода), а обёртка
    /// не даёт HidSharp протекать в код вызывающей стороны: потребителю достаточно передать
    /// в SetProfile() то, что вернул SearchKeyboards(). Кроме того, она оставляет место для
    /// дополнительного состояния в будущем без изменения сигнатур публичных методов.
    /// </remarks>
    public class GmmkKeyboardDevice
    {
        public HidDevice Device { get; internal set; }
        public string DevicePath => Device?.DevicePath;
    }

    /// <summary>
    /// Controller for interacting with GMMK keyboards.
    /// Контроллер для взаимодействия с клавиатурами GMMK.
    /// </summary>
    /// <remarks>
    /// Public entry point of the library. Typical usage from external code:
    /// <code>
    /// var keyboards = GmmkController.SearchKeyboards();
    /// if (keyboards.Count > 0) GmmkController.SetProfile(keyboards[0], 2);
    /// </code>
    /// Note that SetProfile() blocks for roughly PacketDelayMs * 9 milliseconds, so callers
    /// should not invoke it from a UI thread.
    ///
    /// Публичная точка входа библиотеки. Типичное использование из внешнего кода:
    /// <code>
    /// var keyboards = GmmkController.SearchKeyboards();
    /// if (keyboards.Count > 0) GmmkController.SetProfile(keyboards[0], 2);
    /// </code>
    /// Учтите, что SetProfile() блокирует поток примерно на PacketDelayMs * 9 миллисекунд,
    /// поэтому вызывать его из UI-потока не следует.
    /// </remarks>
    public static class GmmkController
    {
        private const int VendorId = 0x0C45;
        private const int ProductId = 0x652F;

        // Expected HID output report payload size for GMMK 1.
        // Ожидаемый размер полезной нагрузки HID output report для GMMK 1.
        private const int PacketSize = 64;

        // Delay between packets, giving the keyboard time to process each one.
        // The value was verified on real GMMK 1 hardware: profile switching works reliably at
        // 30 ms. A full switch therefore costs roughly PacketDelayMs * 9 ms. Lower values were
        // not validated — if you reduce this, re-test on a physical keyboard, because the device
        // silently ignores packets it has not finished processing rather than reporting an error.
        //
        // Задержка между пакетами, чтобы клавиатура успевала обработать каждый.
        // Значение проверено на реальной GMMK 1: при 30 мс переключение профиля работает стабильно.
        // Полное переключение занимает примерно PacketDelayMs * 9 мс. Меньшие значения не
        // проверялись — при уменьшении обязательно протестируйте на физической клавиатуре, так как
        // устройство молча игнорирует необработанные пакеты, а не сообщает об ошибке.
        private const int PacketDelayMs = 30;

        // ---------------------------------------------------------------------------------
        // All packets below were obtained by reverse engineering the USB traffic of the
        // official Glorious software: the exchange was captured while switching profiles and
        // then replayed. Their internal field layout is therefore undocumented and unknown,
        // which is why the bytes are kept as raw literals instead of named structures.
        // Do not "clean up" or reorder these values: the keyboard rejects any deviation.
        //
        // Все пакеты ниже получены реверс-инжинирингом USB-трафика официальной программы
        // Glorious: обмен был записан во время переключения профилей и затем воспроизведён.
        // Внутренняя структура полей не документирована и неизвестна, поэтому байты хранятся
        // как сырые литералы, а не как именованные структуры.
        // Не «причёсывайте» и не переставляйте эти значения: клавиатура отвергает любое отклонение.
        // ---------------------------------------------------------------------------------

        // Initialization / handshake packets sent before the profile packet.
        // Пакеты инициализации (рукопожатия), отправляемые перед пакетом профиля.
        private static readonly byte[] p04_2f = BuildPacket("04 2f 00 03 2c");
        private static readonly byte[] p04_01 = BuildPacket("04 01 00 01");
        private static readonly byte[] p04_3d = BuildPacket("04 3d 00 05 38");
        private static readonly byte[] p04_67 = BuildPacket("04 67 00 05 38 2a");
        private static readonly byte[] p04_91 = BuildPacket("04 91 00 05 38 54");
        private static readonly byte[] p04_02 = BuildPacket("04 02 00 02");

        // Payload block that accompanies the handshake; contents are opaque.
        // Блок данных, сопровождающий рукопожатие; содержимое непрозрачно.
        private static readonly byte[] p04_91_data = BuildPacket(
            "04 91 00 05 38 54 00 00 06 04 02 00 00 00 ff ff 00 00 00 00 00 00 00 03 00 00 00 00 00 00 00 00 " +
            "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 05 a2 a2 02 02 29 02 02 3a 02 02 3b");

        /// <summary>
        /// The final packet that actually activates profile 1, 2 or 3 on the keyboard.
        /// Финальный пакет, который собственно активирует профиль 1, 2 или 3 на клавиатуре.
        /// </summary>
        /// <remarks>
        /// Captured per profile from the official software. Two bytes vary with the profile:
        /// offset 18 holds the profile index (0x00 / 0x01 / 0x02), and offset 1 differs as well
        /// (0x3d / 0x33 / 0x3f) — most likely a checksum or length field, which is why the index
        /// cannot simply be patched into a single shared template. The remaining differences in
        /// profile 2 (including its greater length) were reproduced verbatim from the capture,
        /// because the meaning of the surrounding fields could not be determined.
        ///
        /// Записаны для каждого профиля из официальной программы. С профилем меняются два байта:
        /// по смещению 18 хранится номер профиля (0x00 / 0x01 / 0x02), и по смещению 1 значение
        /// тоже отличается (0x3d / 0x33 / 0x3f) — вероятнее всего, это контрольная сумма или поле
        /// длины, поэтому нельзя просто подставлять номер в один общий шаблон. Остальные отличия
        /// профиля 2 (включая его большую длину) воспроизведены дословно из записи трафика,
        /// так как назначение соседних полей установить не удалось.
        /// </remarks>
        private static readonly Dictionary<int, byte[]> ProfileConfigPackets = new Dictionary<int, byte[]>
        {
            // Profile 1 / Профиль 1
            { 1, BuildPacket("04 3d 02 04 2c 00 00 00 06 04 00 00 00 00 ff ff 00 00 00 00 00 00 00 03") },
            // Profile 2 / Профиль 2
            { 2, BuildPacket("04 33 03 04 2c 00 00 00 06 04 00 00 00 ff fb f0 00 00 01 00 00 00 00 03 00 00 00 01 00 00 00 00 " +
                             "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 06 04") },
            // Profile 3 / Профиль 3
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
