using System;
using System.Collections.Generic;
using System.Linq;
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
            using (PCSC.Monitoring.ISCardMonitor monitor = MonitorFactory.Instance.Create(SCardScope.System))
            {
                using (PCSC.ISCardContext context = ContextFactory.Instance.Establish(SCardScope.System))
                {
                    Console.WriteLine("This program will monitor all SmartCard readers and display all status changes.");

                    string[] readerNames = context.GetReaders();

                    if (IsEmpty(readerNames))
                    {
                        Console.Error.WriteLine("You need at least one reader in order to run this programm.");
                        Console.ReadKey();
                        return;
                    }
                    string readerName = ChooseReader(readerNames);

                    //string readerNames = "ACS ACR1252 1S CL Reader PICC 0";

                    AttachToAllEvents(monitor); // Remember to detach, if you use this in production!

                    monitor.Start(readerName);

                    using (PCSC.Iso7816.IsoReader isoReader = new IsoReader(
                        context: context,
                        readerName: readerName,
                        mode: SCardShareMode.Shared,
                        protocol: SCardProtocol.Any,
                        releaseContextOnDispose: false))
                    {
                        PCSC_Connection.MifareCard card = new MifareCard(isoReader);
                        string[] menuItems = new string[] { "[1] - Загрузить ключ", "[2] - Аутентификация блока", "[3] - Чтение блока", "[4] - Запись блока", "[5] - Считать UID", "[Shift-Q] - Выход" };
                        List<byte> keyNumbers = new List<byte>();

                        while (true)
                        {

                            foreach (string str in menuItems)
                            {
                                Console.WriteLine(str);
                            }

                            System.ConsoleKeyInfo key = Console.ReadKey(true);

                            if (key.Key == ConsoleKey.D1)
                            {
                                keyNumbers.Add(loadKey(card));
                                Console.WriteLine(Convert.ToByte(keyNumbers[0]));
                            }

                            if (key.Key == ConsoleKey.D2)
                            {
                                AuthenticateBlock(card, keyNumbers);
                            }

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

                            if (key.Key == ConsoleKey.D5)
                            {
                                ReadUid(card);
                            }

                            if (ExitRequested(key))
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }

        private static string ChooseReader(IList<string> readerNames)
        {
            Console.WriteLine(new string('=', 79));

            // Show available readers.
            Console.WriteLine("Available readers: ");
            for (var i = 0; i < readerNames.Count; i++)
            {
                Console.WriteLine($"[{i}] {readerNames[i]}");
            }

            // Ask the user which one to choose.
            Console.WriteLine("Choose reader:");

            string line = Console.ReadLine();

            if (int.TryParse(line, out int choice) && (choice >= 0) && (choice <= readerNames.Count - 1))
            {
                return readerNames[choice];
            }

            Console.WriteLine("An invalid number has been entered.");
            Console.ReadKey();

            return null;
        }

        private static void ShowUserInfo(IEnumerable<string> readerNames)
        {
            foreach (string reader in readerNames)
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
            try
            {
                bool loadKeySuccessful = card.LoadKey(KeyStructure.NonVolatileMemory, keyNum, scanKey(card, keyNum));
            }
            catch
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

        private static void AuthenticateBlock(PCSC_Connection.MifareCard card, List<byte> keyNumbers)
        {
            byte keyNumber;
            Console.WriteLine("Выберете ключ для Аутентификации:");
            try
            {
               keyNumber = keyNumbers[Convert.ToInt32(Console.ReadLine())];
            }
            catch
            {
                throw new Exception("Неверный номер ключа");
            }

            Console.WriteLine("Выберете блок для Аутентификации:");
            byte chosenBlock = Convert.ToByte(Console.ReadLine());

            bool authSuccessful = card.Authenticate(MSB, chosenBlock, KeyType.KeyA, keyNumber);
            if (!authSuccessful)
            {
                throw new Exception("AUTHENTICATE failed.");
            }
            Console.WriteLine($"Блок: {chosenBlock:X2} Аутентифицирован\n", chosenBlock);
        }

        private static void ReadCard(PCSC_Connection.MifareCard card, byte chosenBlock)
        {
            byte[] result = card.ReadBinary(MSB, chosenBlock, 16);
            Console.WriteLine("Значение в блока: {0}",
                (result != null)
                    ? BitConverter.ToString(result)
                    : null);
        }

        private static void WriteCard(PCSC_Connection.MifareCard card, byte chosenBlock)
        {
            ReadCard(card, chosenBlock);
            Console.WriteLine("Хотите перезаписать? y/n");
            ConsoleKeyInfo key = Console.ReadKey(true);
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

                bool updateSuccessful = card.UpdateBinary(MSB, chosenBlock, DATA_TO_WRITE);

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
            byte[] uid = card.GetData();

            if (uid != null)
            {
                Console.WriteLine("\nUID: {0}\n", BitConverter.ToString(uid));
            }
            else
            {
                Console.WriteLine("UID: uid not found\n");
            }

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
