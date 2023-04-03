using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PCSC;
using PCSC.Iso7816;
using PCSC.Exceptions;
using PCSC.Monitoring;
using PCSC.Utils;
using System.Text.RegularExpressions;

namespace PCSC_Connection
{
    class Program
    {
        private const byte MSB = 0x00;
        public static void Main()
        {
            using (var monitor = MonitorFactory.Instance.Create(SCardScope.System))
            {
                using (var context = ContextFactory.Instance.Establish(SCardScope.System))
                {
                    Console.WriteLine("This program will monitor all SmartCard readers and display all status changes.");

                    var readerNames = "ACS ACR1252 1S CL Reader PICC 0";

                    AttachToAllEvents(monitor); // Remember to detach, if you use this in production!

                    monitor.Start(readerNames);

                    using (var isoReader = new IsoReader(
                        context: context,
                        readerName: readerNames,
                        mode: SCardShareMode.Shared,
                        protocol: SCardProtocol.Any,
                        releaseContextOnDispose: false))
                    {
                        var card = new MifareCard(isoReader);

                        while (true)
                        {
                            Console.WriteLine("[1] - Загрузить ключ");
                            Console.WriteLine("[2] - Аутентификация блока");
                            Console.WriteLine("[3] - Чтение блока");
                            Console.WriteLine("[4] - Запись блока");
                            Console.WriteLine("[5] - Считать UID");
                            Console.WriteLine("[Shift-Q] - Выход");


                            byte keyNumber = 0;
                            var key = Console.ReadKey(true);
                            if (key.Key == ConsoleKey.D1) keyNumber = loadKey(card);
                            if (key.Key == ConsoleKey.D2) AuthenticateBlock(card, keyNumber);
                            if (key.Key == ConsoleKey.D3)
                            {
                                Console.WriteLine("Выберете блок для чтения:");
                                ReadCard(card, Convert.ToByte(Console.ReadLine()));
                            }
                            if (key.Key == ConsoleKey.D4)
                            {
                                Console.WriteLine("Выберете блок для записи:");
                                WriteCard(card, Convert.ToByte(Console.ReadLine()));
                            }
                            if (key.Key == ConsoleKey.D5) ReadUid(card);

                            if (ExitRequested(key)) break;

                        }
                    }
                }
            }
        }

        private static void ShowUserInfo(IEnumerable<string> readerNames)
        {
            foreach (var reader in readerNames)
            {
                Console.WriteLine($"Start monitoring for reader {reader}.");
            }
        }

        private static void AttachToAllEvents(ISCardMonitor monitor)
        {
            // Point the callback function(s) to the anonymous & static defined methods below.
            monitor.CardInserted += (sender, args) => DisplayEvent("CardInserted", args);
            monitor.CardRemoved += (sender, args) => DisplayEvent("CardRemoved", args);
            monitor.Initialized += (sender, args) => DisplayEvent("Initialized", args);
            monitor.MonitorException += MonitorException;
        }

        private static void DisplayEvent(string eventName, CardStatusEventArgs unknown)
        {
            Console.WriteLine(">> {0} Event for reader: {1}", eventName, unknown.ReaderName);
            Console.WriteLine("State: {0}\n", unknown.State);
        }

        private static void MonitorException(object sender, PCSCException ex)
        {
            Console.WriteLine("Monitor exited due an error:");
            Console.WriteLine(SCardHelper.StringifyError(ex.SCardError));
        }

        private static byte loadKey(PCSC_Connection.MifareCard card)
        {
            byte keyNum = chooseKeyNum();
            bool loadKeySuccessful = card.LoadKey(KeyStructure.NonVolatileMemory, keyNum, scanKey(card, keyNum));

            if (!loadKeySuccessful)
            {
                throw new Exception("LOAD KEY failed.");
            }
            Console.WriteLine("Ключ успешно загружен\n");
            return keyNum;
        }

        private static byte chooseKeyNum()
        {
            byte result = 32; // кол-во ключей не более 1F
            while (result == 32 || result > 31)
            {
                Console.WriteLine("Укажите номер ключа [0-31]:");
                try
                {
                    result = Convert.ToByte(Console.ReadLine());
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine("Error message: {0} ({1})\n", exception.Message, exception.GetType());
                }
            }
            return result;
        }

        private static byte[] scanKey(PCSC_Connection.MifareCard card, byte keyNum)
        {
            string inputKeyValue = "0";
            while (inputKeyValue.Length != 12 || Regex.IsMatch(inputKeyValue, @"[^a-fA-F0-9]"))
            {
                Console.WriteLine("Введите ключ [Формат HEX, 12 символов]:");
                inputKeyValue = Console.ReadLine();
                try
                {
                    if (inputKeyValue.Length != 12 || inputKeyValue == null || Regex.IsMatch(inputKeyValue, @"[^a-fA-F0-9]"))
                    {
                        throw new Exception("Invalid key format!");
                    }
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine("Error message: {0} ({1})\n", exception.Message, exception.GetType());
                }
            }
            return StringToByteArray(inputKeyValue);
        }

        private static void AuthenticateBlock(PCSC_Connection.MifareCard card, byte keyNumber)
        {
            Console.WriteLine("Выберете блок для Аутентификации:");
            byte chosenBlock = Convert.ToByte(Console.ReadLine());
            var authSuccessful = card.Authenticate(MSB, chosenBlock, KeyType.KeyA, keyNumber);
            if (!authSuccessful)
            {
                throw new Exception("AUTHENTICATE failed.");
            }
            Console.WriteLine($"Блок: {0:X2} Аутентифицирован\n", chosenBlock);
        }

        private static void ReadCard(PCSC_Connection.MifareCard card, byte chosenBlock)
        {
            var result = card.ReadBinary(MSB, chosenBlock, 16);
            Console.WriteLine("Значение в блока: {0}",
                (result != null)
                    ? BitConverter.ToString(result)
                    : null);
        }
        private static void WriteCard(PCSC_Connection.MifareCard card, byte chosenBlock)
        {
            ReadCard(card, chosenBlock);
            Console.WriteLine("Хотите перезаписать? y/n");
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Y)
            {

                string inputValue = "0";
                while (inputValue.Length != 32 || Regex.IsMatch(inputValue, @"[^a-fA-F0-9]"))
                {
                    Console.WriteLine("Введите данные для перезаписи [32 вимвола]");
                    inputValue = Console.ReadLine();
                    try
                    {
                        if (inputValue.Length != 32 || inputValue == null || Regex.IsMatch(inputValue, @"[^a-fA-F0-9]"))
                        {
                            throw new Exception("Invalid key format!");
                        }
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine("Error message: {0} ({1})\n", exception.Message, exception.GetType());
                    }
                }
                byte[] DATA_TO_WRITE = StringToByteArray(inputValue);

                var updateSuccessful = card.UpdateBinary(MSB, chosenBlock, DATA_TO_WRITE);

                if (!updateSuccessful)
                {
                    throw new Exception("UPDATE BINARY failed.");
                }
                Console.WriteLine("Запись прошла успешено\n");
            }
            else
            {
                Console.WriteLine("Перезапись отменена\n");
            }
        }

        private static void ReadUid(PCSC_Connection.MifareCard card)
        {
            //var uid = card.GetData();
            //Console.WriteLine("UID: {0}\n",
            //    (uid != null)
            //        ? BitConverter.ToString(uid)
            //        : '0');
        }

        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

        private static bool IsEmpty(ICollection<string> readerNames) =>
            readerNames == null || readerNames.Count < 1;

        private static bool ExitRequested(ConsoleKeyInfo key) =>
            key.Modifiers == ConsoleModifiers.Shift &&
            key.Key == ConsoleKey.Q;
    }
}
