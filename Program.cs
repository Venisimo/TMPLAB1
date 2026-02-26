using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TMPLAB1
{
    internal class Program
    {
        // Заголовок PRD
        const int SIZE_SIGNATURE = 2;
        const int SIZE_RECORDLEN = 2;
        const int SIZE_P_FIRSTREC = 4;
        const int SIZE_P_FREESPACE = 4;
        const int SIZE_NAMESPEC = 16;
        const int RECORD_NAME_MAXLEN = 64;
        const int HEADER_SIZE = SIZE_SIGNATURE + SIZE_RECORDLEN + SIZE_P_FIRSTREC + SIZE_P_FREESPACE + SIZE_NAMESPEC;

        // Смещения в заголовке
        const int OFFSET_RECORDLEN = SIZE_SIGNATURE;                     // 2
        const int OFFSET_P_FIRSTREC = OFFSET_RECORDLEN + SIZE_RECORDLEN; // 4
        const int OFFSET_P_FREESPACE = OFFSET_P_FIRSTREC + SIZE_P_FIRSTREC; // 8
        const int OFFSET_NAMESPEC = OFFSET_P_FREESPACE + SIZE_P_FREESPACE;  // 12

        enum Command { CMD_CREATE, CMD_OPEN, CMD_SAVE, CMD_UNKNOWN, CMD_INPUT, CMD_PRINT };
        enum Type { PRODUCT, ASSEMBLY, DETAIL, UNKNOWN };
        

        public static void Create(string fileName)
        {
            string pureName = Path.GetFileNameWithoutExtension(fileName);
            string prsName = pureName + ".prs";

            if (pureName.Length > 16)
            {
                Console.WriteLine("Ошибка: Максимальная длина имени компонента — 16 символов!");
                return;
            }

            if (File.Exists(fileName))
            {
                Console.WriteLine($"Файл {fileName} уже существует!");
                while (true)
                {
                    Console.Write("Хотите пересоздать файл с данным именем? (y/n): ");
                    char res = Console.ReadKey(true).KeyChar;
                    Console.WriteLine(res);

                    if (res == 'n' || res == 'N') return;
                    if (res == 'y' || res == 'Y') break;
                }
            }

            // Создаем заголовок
            FileHeaderPRD header = new FileHeaderPRD
            {
                RecordLen = RECORD_NAME_MAXLEN,
                p_FirstRec = -1,         // пока нет записей
                p_FreeSpace = 0,         // пока свободное место не используется
                NameSpec = Encoding.ASCII.GetBytes(prsName.PadRight(16))
            };

            // Создаем PRD файл и записываем заголовок
            using (BinaryWriter bw = new BinaryWriter(File.Create(fileName)))
            {
                bw.Write(header.Signature);     // 2 байта "PS"
                bw.Write(header.RecordLen);     // 2 байта
                bw.Write(header.p_FirstRec);    // 4 байта
                bw.Write(header.p_FreeSpace);   // 4 байта
                bw.Write(header.NameSpec);      // 16 байт
            }

            Console.WriteLine($"Файл {fileName} создан!");

            // Создаем пустой PRS файл с заголовком (p_FirstRec = -1, p_FreeSpace = 0)
            using (BinaryWriter bw = new BinaryWriter(File.Create(prsName)))
            {
                bw.Write(-1);  // p_FirstRec
                bw.Write(0);   // p_FreeSpace
            }

            Console.WriteLine($"Файл {prsName} создан!");
        }

        private static void ReadPRD(string fileName)
        {
            try
            {
                using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (BinaryReader br = new BinaryReader(fs))
                {

                    fs.Seek(OFFSET_P_FIRSTREC, SeekOrigin.Begin);
                    int p_FirstRec = br.ReadInt32();

                    if (p_FirstRec == -1)
                    {
                        Console.WriteLine("Записей нет.");
                        return;
                    }

                    int offset = p_FirstRec;

                    while (offset != -1 && offset < fs.Length)
                    {
                        fs.Seek(offset, SeekOrigin.Begin);

                        byte flagDelete = br.ReadByte();
                        int p_FirstComp = br.ReadInt32();
                        int p_Next = br.ReadInt32();
                        ushort nameLen = br.ReadUInt16();

                        byte[] nameBytes = br.ReadBytes(nameLen);
                        string name = Encoding.UTF8.GetString(nameBytes);

                        Console.WriteLine($"Offset: {offset} | Deleted: {(flagDelete == 0xFF ? "Yes" : "No")} | FirstComp: {p_FirstComp} | Next: {p_Next} | Name: {name}");

                        offset = p_Next;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка чтения: " + ex.Message);
            }
        }
        static string Open(string fileName)
        {
            if (!File.Exists(fileName))
                throw new Exception($"Файла {fileName} не существует!");

            using (FileStream fs = new FileStream(fileName, FileMode.Open))
            {
                byte[] signatureBytes = new byte[2];

                int read = fs.Read(signatureBytes, 0, 2);

                if (read < 2)
                    throw new Exception("Файл слишком короткий!");

                string signature = Encoding.ASCII.GetString(signatureBytes);

                if (signature != "PS")
                {
                    Console.WriteLine("Файл имеет неверную сигнатуру!");
                }
                else
                {
                    Console.WriteLine($"Файл {fileName} открыт успешно!");
                }
                return fileName;
            }
        }

        static Command GetCommandType(string cmd)
        {
            if (cmd == "create") return Command.CMD_CREATE;
            if (cmd == "open") return Command.CMD_OPEN;
            if (cmd == "save") return Command.CMD_SAVE;
            if (cmd == "input") return Command.CMD_INPUT;
            return Command.CMD_UNKNOWN;
        }
        static Type GetComponentType(string component)
        {
            if (component == "Изделие") return Type.PRODUCT;
            if (component == "Узел") return Type.ASSEMBLY;
            if (component == "Деталь") return Type.DETAIL;
            return Type.UNKNOWN;
        }

        static void Input(string argument, string fileName)
        {
            string[] parts = argument
                .Replace("(", "")
                .Replace(")", "")
                .Replace(",", "")
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
                throw new Exception("Формат: input <имя> <тип>");

            string name = parts[0];
            Type type = GetComponentType(parts[1]);

            if (type == Type.UNKNOWN) 
                throw new Exception("Неизвестный тип компонента");

            if (name.Length > RECORD_NAME_MAXLEN) 
                throw new Exception("Превышена максимальная длина имени");

            byte[] nameBytes = Encoding.UTF8.GetBytes(name);

            using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader br = new BinaryReader(fs))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                // --- Заголовок ---
                fs.Seek(OFFSET_P_FIRSTREC, SeekOrigin.Begin);
                int p_FirstRec = br.ReadInt32();

                fs.Seek(OFFSET_P_FREESPACE, SeekOrigin.Begin);
                int p_FreeSpace = br.ReadInt32();

                // --- Новая запись в конец файла ---
                fs.Seek(0, SeekOrigin.End);
                int newOffset = (int)fs.Position;

                bw.Write((byte)0);       // FlagDelete
                bw.Write(-1);            // p_FirstComp
                bw.Write(p_FirstRec);    // p_Next = предыдущая первая запись
                bw.Write((ushort)nameBytes.Length); // длина имени
                bw.Write(nameBytes);     // имя
                bw.Flush();

                // --- Обновление заголовка ---
                fs.Seek(OFFSET_P_FIRSTREC, SeekOrigin.Begin);
                bw.Write(newOffset); // новая запись становится первой

                fs.Seek(OFFSET_P_FREESPACE, SeekOrigin.Begin);
                int newFreeSpace = p_FreeSpace + 1 + 4 + 4 + nameBytes.Length; // flag + p_FirstComp + p_Next + имя
                bw.Write(newFreeSpace);
            }

            Console.WriteLine($"Компонент '{name}' ({parts[1]}) добавлен.");
        }

        static void Main(string[] args)
        {
            string currentFile = null;
            bool isOpen = false;

            Console.WriteLine("Система управления спецификациями (PRD)");

            while (true)
            {
                Console.Write("PS> ");
                string commandLine = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(commandLine))
                    continue;

                string[] parts = commandLine.Split(new[] { ' ' }, 2);

                string command = parts[0].ToLower();
                string argument = parts.Length > 1 ? parts[1] : null;

                try
                {
                    switch (command)
                    {
                        case "create":
                            Create(argument);
                            break;

                        case "open":
                            currentFile = Open(argument);
                            isOpen = true;
                            break;

                        case "input":
                            if (!isOpen)
                                throw new Exception("Файл не открыт");
                            Input(argument, currentFile);
                            break;
                        case "print":
                            ReadPRD(argument);
                            break;
                        case "exit":
                            return;

                        default:
                            Console.WriteLine("Неизвестная команда.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка: {ex.Message}");
                }
            }
        }
    }
}