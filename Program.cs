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

                    AttachToAllEvents(monitor);
                    monitor.Start(readerName);

                    using (PCSC.Iso7816.IsoReader isoReader = new IsoReader(
                        context: context,
                        readerName: readerName,
                        mode: SCardShareMode.Shared,
                        protocol: SCardProtocol.Any,
                        releaseContextOnDispose: false))
                    {
                        PCSC_Connection.MifareCard card = new MifareCard(isoReader);

                        MainMenu(card);
                    }
                }
            }
        }

        /// <summary>
        /// Основоное меню приложения
        /// </summary>
        /// <param name="card"></param>
        private static void MainMenu(PCSC_Connection.MifareCard card)
        {
            List<byte> keyNumbers = new List<byte>();

            MenuList();
            System.ConsoleKeyInfo key = ValidMenuInput();

            if (key.Key == ConsoleKey.D1 || key.Key == ConsoleKey.NumPad1)
            {
                keyNumbers.Add(loadKey(card));
                Console.WriteLine(Convert.ToByte(keyNumbers[0]));
                MainMenu(card);
            }

            if (key.Key == ConsoleKey.D2 || key.Key == ConsoleKey.NumPad2)
            {
                AuthenticateBlock(card, keyNumbers);
                MainMenu(card);
            }

            if (key.Key == ConsoleKey.D3 || key.Key == ConsoleKey.NumPad3)
            {
                ReadCard(card);
                MainMenu(card);
            }

            if (key.Key == ConsoleKey.D4 || key.Key == ConsoleKey.NumPad4)
            {
                WriteCard(card, Convert.ToByte(Console.ReadLine()));
                MainMenu(card);
            }

            if (key.Key == ConsoleKey.D5 || key.Key == ConsoleKey.NumPad5)
            {
                ReadUid(card);
                MainMenu(card);
            }

            if (ExitRequested(key))
            {
            }
        }

        /// <summary>
        /// Вывод основного меню
        /// </summary>
        private static void MenuList()
        {
            string[] menuItems = new string[] { "[1] - Загрузить ключ", "[2] - Аутентификация блока", "[3] - Чтение блока", "[4] - Запись блока", "[5] - Считать UID", "[Shift-Q] - Выход" };
            foreach (string str in menuItems)
            {
                Console.WriteLine(str);
            }
            Console.WriteLine("Выберите действие:");
        }

        /// <summary>
        /// Проверка ввода для выбора пункта меню
        /// </summary>
        /// <returns>Валидный ввод или запрашивает ввод повторно</returns>
        private static System.ConsoleKeyInfo ValidMenuInput()
        {
            System.ConsoleKeyInfo input = Console.ReadKey(true);

            if (input.Key.Equals(ConsoleKey.D1) || input.Key.Equals(ConsoleKey.NumPad1) ||
                input.Key.Equals(ConsoleKey.D2) || input.Key.Equals(ConsoleKey.NumPad2) ||
                input.Key.Equals(ConsoleKey.D3) || input.Key.Equals(ConsoleKey.NumPad3) ||
                input.Key.Equals(ConsoleKey.D4) || input.Key.Equals(ConsoleKey.NumPad4) ||
                input.Key.Equals(ConsoleKey.D5) || input.Key.Equals(ConsoleKey.NumPad5) ||
                ExitRequested(input)
                )
            {
                return input;
            }
            else
            {
                Console.WriteLine("\nТакой функции не существует!\nВведите номер из существующего списка:");
                MenuList();
                return ValidMenuInput();
            }
        }

        /// <summary>
        /// Выбор ридера для работы с картой
        /// </summary>
        /// <param name="readerNames">Список всех полключенных ридеров</param>
        /// <returns></returns>
        private static string ChooseReader(IList<string> readerNames)
        {
            Console.WriteLine(new string('=', 79));

            // Show available readers.
            Console.WriteLine("Доступные ридеры:");
            for (var i = 0; i < readerNames.Count; i++)
            {
                Console.WriteLine($"[{i}] {readerNames[i]}");
            }

            // Ask the user which one to choose.
            Console.WriteLine("Выберите ридер:");

            string line = Console.ReadLine();

            if (int.TryParse(line, out int choice) && (choice >= 0) && (choice <= readerNames.Count - 1))
            {
                return readerNames[choice];
            }

            Console.WriteLine("Выбранный ридер не существует!");

            return ChooseReader(readerNames);
        }

        /// <summary>
        /// Мониторинг событий на ридере
        /// </summary>
        /// <param name="monitor"></param>
        private static void AttachToAllEvents(ISCardMonitor monitor)
        {
            // Point the callback function(s) to the anonymous & static defined methods below.
            monitor.CardInserted += (sender, args) => DisplayEvent("CardInserted", args);
            monitor.CardRemoved += (sender, args) => DisplayEvent("CardRemoved", args);
            monitor.Initialized += (sender, args) => DisplayEvent("Initialized", args);
            monitor.MonitorException += MonitorException;
        }

        /// <summary>
        /// Отображение событий в консоле
        /// </summary>
        /// <param name="eventName">Наименование события</param>
        /// <param name="unknown">Ридер, на котором произошло событие</param>
        private static void DisplayEvent(string eventName, CardStatusEventArgs unknown)
        {
            Console.WriteLine(">> {0} Event for reader: {1}", eventName, unknown.ReaderName);
            Console.WriteLine("State: {0}\n", unknown.State);
        }

        /// <summary>
        /// Обработка неописанных событий
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="ex"></param>
        private static void MonitorException(object sender, PCSCException ex)
        {
            Console.WriteLine("Monitor exited due an error:");
            Console.WriteLine(SCardHelper.StringifyError(ex.SCardError));
        }

        /// <summary>
        /// Загрузка ключа
        /// </summary>
        /// <param name="card"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Выбор номера ключа
        /// </summary>
        /// <returns></returns>
        private static byte chooseKeyNum()
        {
            byte result; // кол-во ключей не более 1F
            Console.WriteLine("Укажите номер ключа [0-31]:");

            try
            {
                result = Convert.ToByte(Console.ReadLine());
            }
            catch
            {
                Console.WriteLine("Задан неверный номер для ключа!");
                return chooseKeyNum();
            }

            return result;
        }

        /// <summary>
        /// Получение значения ключа
        /// </summary>
        /// <param name="card"></param>
        /// <param name="keyNum">Номер ключа из chooseKeyNum()</param>
        /// <returns></returns>
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

        /// <summary>
        /// Аутентификация блока
        /// </summary>
        /// <param name="card"></param>
        /// <param name="keyNumbers">Список всех заданных ключей</param>
        private static void AuthenticateBlock(PCSC_Connection.MifareCard card, List<byte> keyNumbers)
        {
            byte keyNumber;
            Console.WriteLine("\nВыберете ключ для Аутентификации:");
            try
            {
                keyNumber = keyNumbers[Convert.ToInt32(Console.ReadLine())];
            }
            catch
            {
                throw new Exception("Неверный номер ключа");
            }

            Console.WriteLine("Выберете тип ключа: А/В?");
            PCSC_Connection.KeyType keyType;
            ConsoleKeyInfo key = Console.ReadKey();

            if (key.Key == ConsoleKey.B)
            {
                keyType = KeyType.KeyB;
            }
            else
            {
                keyType = KeyType.KeyA;
            }

            Console.WriteLine("\nВыберете блок для Аутентификации:");
            byte chosenBlock = Convert.ToByte(Console.ReadLine());

            bool authSuccessful = card.Authenticate(MSB, chosenBlock, keyType, keyNumber);
            if (!authSuccessful)
            {
                throw new Exception("AUTHENTICATE failed.");
            }
            Console.WriteLine($"\nБлок: {chosenBlock:X2} Аутентифицирован\n", chosenBlock);
        }

        /// <summary>
        /// Чтение указанного блока
        /// </summary>
        /// <param name="card"></param>
        /// <param name="chosenBlock">Номер блока для чтения</param>
        private static void ReadCard(PCSC_Connection.MifareCard card)
        {
            Console.WriteLine("\nВыберете блок для чтения:");
            byte[] result = card.ReadBinary(MSB, Convert.ToByte(Console.ReadLine()), 16);

            if (result != null)
            {
                Console.WriteLine("Значение в блока: {0}", BitConverter.ToString(result));
            }
            else
            {
                Console.WriteLine("Чтение не удалось\n");
            }
        }

        /// <summary>
        /// Запись в указанный блок
        /// </summary>
        /// <param name="card"></param>
        /// <param name="chosenBlock">Номер блока для записи</param>
        private static void WriteCard(PCSC_Connection.MifareCard card, byte chosenBlock)
        {
            Console.WriteLine("\nВыберете блок для записи:");
            ReadCard(card);
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

        /// <summary>
        /// Чтение UID карты
        /// </summary>
        /// <param name="card"></param>
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

        /// <summary>
        /// Преобразование строки с ввода консоли в массив байт
        /// </summary>
        /// <param name="hex">Строка полученная на вводе</param>
        /// <returns>Массив байт</returns>
        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

        /// <summary>
        /// Проверка на наличие подключенных ридеров
        /// </summary>
        /// <param name="readerNames">Список ридеров</param>
        /// <returns></returns>
        private static bool IsEmpty(ICollection<string> readerNames)
        {
            return readerNames == null || readerNames.Count < 1;
        }

        /// <summary>
        /// Условия для выхода из программы
        /// </summary>
        /// <param name="key">Введенный ключ с клавиатуры</param>
        /// <returns></returns>
        private static bool ExitRequested(ConsoleKeyInfo key)
        {
            return key.Modifiers == ConsoleModifiers.Shift && key.Key == ConsoleKey.Q;
        }
    }
}
