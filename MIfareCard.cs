using PCSC;
using PCSC.Iso7816;
using System;
using System.Diagnostics;

namespace PCSC_Connection
{
    public class MifareCard
    {
        private const byte CUSTOM_CLA = 0xFF;
        private readonly IIsoReader _isoReader;

        public MifareCard(IIsoReader isoReader)
        {
            _isoReader = isoReader ?? throw new ArgumentNullException(nameof(isoReader));
        }

        /// <summary>
        /// Формирование и отправка APDU команды GetData
        /// </summary>
        /// <returns></returns>
        public byte[] GetData()
        {
            CommandApdu getDataCmd = new CommandApdu(IsoCase.Case2Short, SCardProtocol.Any)
            {
                CLA = CUSTOM_CLA,
                Instruction = InstructionCode.GetData,
                P1 = 0x00,
                P2 = 0x00
            };

            Response response = _isoReader.Transmit(getDataCmd);
            return IsSuccess(response)
                    ? response.GetData() ?? new byte[0]
                    : null;
        }

        /// <summary>
        /// Отправка APDU LoadKey
        /// </summary>
        /// <param name="keyStructure">Тип ключа</param>
        /// <param name="keyNumber">Номер ключа</param>
        /// <param name="key">Значение ключа</param>
        /// <returns></returns>
        public bool LoadKey(KeyStructure keyStructure, byte keyNumber, byte[] key)
        {
            CommandApdu loadKeyCmd = new CommandApdu(IsoCase.Case3Short, SCardProtocol.Any)
            {
                CLA = CUSTOM_CLA,
                Instruction = InstructionCode.ExternalAuthenticate,
                P1 = (byte)keyStructure,
                P2 = keyNumber,
                Data = key
            };

            Console.WriteLine("KEY:" + BitConverter.ToString(loadKeyCmd.Data));
            Debug.WriteLine($"Load Authentication Keys: {BitConverter.ToString(loadKeyCmd.ToArray())}");
            Response response = _isoReader.Transmit(loadKeyCmd);
            Debug.WriteLine($"SW1 SW2 = {response.SW1:X2} {response.SW2:X2}");
            return IsSuccess(response);
        }

        /// <summary>
        /// Формирование APDU для аутентификации
        /// </summary>
        /// <param name="msb"></param>
        /// <param name="chosenBlock">Аутентифицируемый блок</param>
        /// <param name="keyType">Тип ключа: A, B</param>
        /// <param name="keyNumber">Номер ключа</param>
        /// <returns></returns>
        public bool Authenticate(byte msb, byte chosenBlock, KeyType keyType, byte keyNumber)
        {
            GeneralAuthenticate authBlock = new GeneralAuthenticate
            {
                Msb = msb,
                Lsb = chosenBlock,
                KeyType = keyType,
                KeyNumber = keyNumber
            };

            CommandApdu authKeyCmd = new CommandApdu(IsoCase.Case3Short, SCardProtocol.Any)
            {
                CLA = CUSTOM_CLA,
                Instruction = InstructionCode.InternalAuthenticate,
                P1 = 0x00,
                P2 = 0x00,
                Data = authBlock.ToArray()
            };

            Debug.WriteLine($"General Authenticate: {BitConverter.ToString(authKeyCmd.ToArray())}");
            Response response = _isoReader.Transmit(authKeyCmd);
            Debug.WriteLine($"SW1 SW2 = {response.SW1:X2} {response.SW2:X2}");

            return (response.SW1 == 0x90) && (response.SW2 == 0x00);
        }

        /// <summary>
        /// Формирование APDU для чтения блока
        /// </summary>
        /// <param name="msb"></param>
        /// <param name="chosenBlock">Блок для чтения</param>
        /// <param name="size">Размер блока(16 байт)</param>
        /// <returns></returns>
        public byte[] ReadBinary(byte msb, byte chosenBlock, int size)
        {
            unchecked
            {
                CommandApdu readBinaryCmd = new CommandApdu(IsoCase.Case2Short, SCardProtocol.Any)
                {
                    CLA = CUSTOM_CLA,
                    Instruction = InstructionCode.ReadBinary,
                    P1 = msb,
                    P2 = chosenBlock,
                    Le = size
                };

                Debug.WriteLine($"Read Binary: {BitConverter.ToString(readBinaryCmd.ToArray())}");
                Response response = _isoReader.Transmit(readBinaryCmd);
                Debug.WriteLine($"SW1 SW2 = {response.SW1:X2} {response.SW2:X2} Data: {BitConverter.ToString(response.GetData())}");

                return IsSuccess(response)
                    ? response.GetData() ?? new byte[0]
                    : null;
            }
        }

        /// <summary>
        /// Формирование APDU для записи блока
        /// </summary>
        /// <param name="msb"></param>
        /// <param name="chosenBlock">Блок для записи</param>
        /// <param name="data">Данные для записи</param>
        /// <returns></returns>
        public bool UpdateBinary(byte msb, byte chosenBlock, byte[] data)
        {
            CommandApdu updateBinaryCmd = new CommandApdu(IsoCase.Case3Short, SCardProtocol.Any)
            {
                CLA = CUSTOM_CLA,
                Instruction = InstructionCode.UpdateBinary,
                P1 = msb,
                P2 = chosenBlock,
                Data = data
            };

            Debug.WriteLine($"Update Binary: {BitConverter.ToString(updateBinaryCmd.ToArray())}");
            Response response = _isoReader.Transmit(updateBinaryCmd);
            Debug.WriteLine($"SW1 SW2 = {response.SW1:X2} {response.SW2:X2}");

            return IsSuccess(response);
        }

        /// <summary>
        /// Проверка, что ответ на поданные APDU команды равен 9000
        /// </summary>
        /// <param name="response">Полученный ответ</param>
        /// <returns></returns>
        private static bool IsSuccess(Response response)
        {
            return (response.SW1 == (byte)SW1Code.Normal) && (response.SW2 == 0x00);
        }
    }
}