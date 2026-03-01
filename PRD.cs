using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

enum Type { PRODUCT, ASSEMBLY, DETAIL, UNKNOWN };

namespace TMPLAB1
{
    public class FileHeaderPRD : IFileHeaderPRD
    {
        public byte[] Signature { get; set; } = new byte[2];
        public ushort RecordLen { get; set; }
        public int p_FirstRec { get; set; }
        public int p_FreeSpace { get; set; }
        public byte[] NameSpec { get; set; } = new byte[16];

        public bool IsOpen { get; set; }
        public string CurrentFileName { get; set; }

        public FileHeaderPRD()
        {
            Signature[0] = (byte)'P';
            Signature[1] = (byte)'S';
            IsOpen = false;
            CurrentFileName = null;
        }

        public void Create(string fileName)
        {
            string pureName = Path.GetFileNameWithoutExtension(fileName);
            string prsName = pureName + ".prs";

            // Проверка расширения файла
            if (!fileName.EndsWith(".prd", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Ошибка: Файл должен иметь расширение .prd. Пример: 'file.prd' ");
            }

            // Проверка длины имени
            if (pureName.Length > 16)
            {
                throw new Exception("Ошибка: Максимальная длина имени компонента — 16 символов!");
            }

            // Проверка существующего файла
            if (File.Exists(fileName))
            {
                Console.WriteLine($"Файл {fileName} уже существует!");

                // Проверяем сигнатуру существующего файла
                try
                {
                    using (BinaryReader br = new BinaryReader(File.OpenRead(fileName)))
                    {
                        byte[] signature = br.ReadBytes(2);

                        if (signature.Length < 2)
                        {
                            throw new Exception("Ошибка: Сигнатура отсутствует");
                        }

                        string signatureStr = Encoding.ASCII.GetString(signature);

                        if (signatureStr != "PS")
                        {
                            throw new Exception($"Ошибка: Неверная сигнатура файла. Ожидание 'PS', получено '{signatureStr}'");
                        }
                    }

                }
                catch (Exception ex)
                {
                    throw new Exception($"Ошибка при чтении файла: {ex.Message}");
                }

                while (true)
                {
                    Console.Write("Хотите пересоздать файл с данным именем? (y/n): ");
                    char res = Console.ReadKey(true).KeyChar;
                    Console.WriteLine(res);

                    if (res == 'n' || res == 'N') return;
                    if (res == 'y' || res == 'Y') break;
                }
            }

            RecordLen = 64;
            p_FirstRec = -1;
            p_FreeSpace = 0;
            NameSpec = Encoding.ASCII.GetBytes(prsName.PadRight(16));

            // Создаем PRD файл и записываем заголовок
            using (BinaryWriter bw = new BinaryWriter(File.Create(fileName)))
            {
                bw.Write(Signature);     // 2 байта "PS"
                bw.Write(RecordLen);     // 2 байта
                bw.Write(p_FirstRec);    // 4 байта
                bw.Write(p_FreeSpace);   // 4 байта
                bw.Write(NameSpec);      // 16 байт
            }

            Console.WriteLine($"Файл {fileName} создан.");

            // Создаем пустой PRS файл
            using (BinaryWriter bw = new BinaryWriter(File.Create(prsName)))
            {
                bw.Write(-1);  // p_FirstRec
                bw.Write(0);   // p_FreeSpace
            }

            Console.WriteLine($"Файл {prsName} создан.");

            // Открываем файл для работы
            CurrentFileName = fileName;
            IsOpen = true;
            Console.WriteLine($"Файл {fileName} открыт для работы.");
        }

        public void Open(string fileName)
        {
            if (!File.Exists(fileName)) throw new Exception($"Файла {fileName} не существует");

            if (!fileName.EndsWith(".prd", StringComparison.OrdinalIgnoreCase)) throw new Exception("Ошибка: Файл должен иметь расширение .prd. Пример: 'file.prd' ");

            try
            {
                using (BinaryReader br = new BinaryReader(File.OpenRead(fileName)))
                {
                    Signature = br.ReadBytes(2);
                    string signatureStr = Encoding.ASCII.GetString(Signature);

                    if (signatureStr != "PS") throw new Exception("Неверная сигнатура файла");

                    RecordLen = br.ReadUInt16();
                    p_FirstRec = br.ReadInt32();
                    p_FreeSpace = br.ReadInt32();
                    NameSpec = br.ReadBytes(16);

                    if (NameSpec.Length < 16) throw new Exception("Файл поврежден: неполный заголовок");
                }


                CurrentFileName = fileName;
                IsOpen = true;
                Console.WriteLine($"Файл {fileName} открыт");

            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка при открытии файла: {ex.Message}");
            }
        }

        private Type GetComponentType(string typeName)
        {
            if (typeName == "Изделие") return Type.PRODUCT;
            if (typeName == "Узел") return Type.ASSEMBLY;
            if (typeName == "Деталь") return Type.DETAIL;
            return Type.UNKNOWN;
        }

        public void Input(string argument)
        {
            if (!IsOpen) throw new Exception("Файл не открыт");

            string[] parts = argument
                .Replace("(", "")
                .Replace(")", "")
                .Replace(",", "")
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2) throw new Exception("Формат: input <имя> <тип>");

            string name = parts[0];
            Type type = GetComponentType(parts[1]);

            if (type == Type.UNKNOWN) throw new Exception("Неизвестный тип компонента");

            if (name.Length > 64) throw new Exception("Превышена максимальная длина имени");

            byte[] nameBytes = Encoding.UTF8.GetBytes(name);

            RecordPRD newRecord = new RecordPRD
            {
                FlagDelete = 0,
                p_FirstComp = -1,
                p_Next = p_FirstRec,
                Name = name
            };

            using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader br = new BinaryReader(fs))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                int currentOffset = p_FirstRec;
                while (currentOffset != -1 && currentOffset < fs.Length)
                {
                    fs.Seek(currentOffset, SeekOrigin.Begin);

                    RecordPRD record = new RecordPRD
                    {
                        FlagDelete = br.ReadByte(),
                        p_FirstComp = br.ReadInt32(),
                        p_Next = br.ReadInt32()
                    };
                    ushort nameLen = br.ReadUInt16();
                    byte[] CheckNameBytes = br.ReadBytes(nameLen);
                    record.Name = Encoding.UTF8.GetString(CheckNameBytes);

                    if (record.Name == name && !record.IsDeleted)
                    {
                        throw new Exception($"Компонент с именем '{name}' уже существует!");
                    }
                    currentOffset = record.p_Next;
                }

                fs.Seek(4, SeekOrigin.Begin);
                int oldP_FirstRec = br.ReadInt32();

                fs.Seek(8, SeekOrigin.Begin);
                int oldP_FreeSpace = br.ReadInt32();

                fs.Seek(0, SeekOrigin.End);
                int newOffset = (int)fs.Position;

                bw.Write(newRecord.FlagDelete);
                bw.Write(newRecord.p_FirstComp);
                bw.Write(newRecord.p_Next);
                bw.Write((ushort)nameBytes.Length);
                bw.Write(nameBytes);
                bw.Flush();

                fs.Seek(4, SeekOrigin.Begin);
                bw.Write(newOffset);

                fs.Seek(8, SeekOrigin.Begin);
                int newFreeSpace = oldP_FreeSpace + 1 + 4 + 4 + nameBytes.Length;
                bw.Write(newFreeSpace);

                p_FirstRec = newOffset;
                p_FreeSpace = newFreeSpace;
            }

            Console.WriteLine($"Компонент '{name}' ({parts[1]}) добавлен.");
        }

        public void Delete(string name)
        {
            if (!IsOpen) throw new Exception("Файл не открыт");

            if (string.IsNullOrEmpty(name)) throw new Exception("Укажите имя компонента для удаления");

            List<RecordPRD> allRecords = new List<RecordPRD>();
            Dictionary<int, RecordPRD> recordsByOffset = new Dictionary<int, RecordPRD>();
            int foundOffset = -1;
            RecordPRD foundRecord = null;

            using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader br = new BinaryReader(fs))
            {
                int currentOffset = p_FirstRec;
                while (currentOffset != -1 && currentOffset < fs.Length)
                {
                    fs.Seek(currentOffset, SeekOrigin.Begin);

                    RecordPRD record = new RecordPRD
                    {
                        FlagDelete = br.ReadByte(),
                        p_FirstComp = br.ReadInt32(),
                        p_Next = br.ReadInt32()
                    };

                    ushort nameLen = br.ReadUInt16();
                    byte[] nameBytes = br.ReadBytes(nameLen);
                    record.Name = Encoding.UTF8.GetString(nameBytes);

                    recordsByOffset[currentOffset] = record;
                    allRecords.Add(record);

                    // Просто ищем первый (и единственный) компонент с нужным именем
                    if (record.Name == name && !record.IsDeleted)
                    {
                        foundOffset = currentOffset;
                        foundRecord = record;
                        break;
                    }

                    currentOffset = record.p_Next;
                }
            }

            if (foundOffset == -1) throw new Exception($"Компонент '{name}' не найден");

            // Проверка на наличие ссылок на удаляемый компонент
            foreach (var kvp in recordsByOffset)
            {
                if (kvp.Value.p_FirstComp == foundOffset && !kvp.Value.IsDeleted)
                    throw new Exception($"Невозможно удалить компонент '{name}': на него есть ссылки из компонента '{kvp.Value.Name}'");
            }

            using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                fs.Seek(foundOffset, SeekOrigin.Begin);
                bw.Write((byte)0xFF); // Помечаем как удаленный
            }

            Console.WriteLine($"Компонент '{name}' помечен как удаленный.");
        }

        public void Restore(string name)
        {
            if (!IsOpen) throw new Exception("Файл не открыт");

            if (string.IsNullOrEmpty(name)) throw new Exception("Укажите имя компонента для восстановления");

            if (name == "*")
            {
                RestoreAll();
                return;
            }

            using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader br = new BinaryReader(fs))
            {
                int currentOffset = p_FirstRec;
                int foundOffset = -1;

                while (currentOffset != -1 && currentOffset < fs.Length)
                {
                    fs.Seek(currentOffset, SeekOrigin.Begin);

                    byte flag = br.ReadByte();

                    int pFirstComp = br.ReadInt32();
                    int pNext = br.ReadInt32();

                    ushort nameLen = br.ReadUInt16();
                    byte[] nameBytes = br.ReadBytes(nameLen);
                    string recordName = Encoding.UTF8.GetString(nameBytes);

                    if (recordName == name)
                    {
                        if (flag != 0xFF) // Проверяем, удален ли
                            throw new Exception($"Компонент '{name}' не удален");

                        foundOffset = currentOffset;
                        break;
                    }

                    currentOffset = pNext;
                }

                if (foundOffset == -1)
                    throw new Exception($"Компонент '{name}' не найден");

                using (BinaryWriter bw = new BinaryWriter(fs))
                {
                    fs.Seek(foundOffset, SeekOrigin.Begin);
                    bw.Write((byte)0x00);
                }

                Console.WriteLine($"Компонент '{name}' восстановлен.");
            }
        }

        private void RestoreAll()
        {
            using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader br = new BinaryReader(fs))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                int currentOffset = p_FirstRec;
                int restoredCount = 0;

                while (currentOffset != -1 && currentOffset < fs.Length)
                {
                    fs.Seek(currentOffset, SeekOrigin.Begin);

                    // Читаем флаг удаления
                    byte flag = br.ReadByte();

                    // Читаем остальные поля
                    int pFirstComp = br.ReadInt32();
                    int pNext = br.ReadInt32();

                    ushort nameLen = br.ReadUInt16();
                    byte[] nameBytes = br.ReadBytes(nameLen);
                    string recordName = Encoding.UTF8.GetString(nameBytes);

                    if (flag == 0xFF)
                    {
                        fs.Seek(currentOffset, SeekOrigin.Begin);
                        bw.Write((byte)0x00);
                        restoredCount++;
                    }

                    currentOffset = pNext;
                }

                Console.WriteLine($"Восстановлено компонентов: {restoredCount}");
            }
        }

        public void Truncate()
        {

            string tempFile = Path.GetTempFileName();
            int newFirstRec = -1;
            int lastValidOffset = -1;
            int removedCount = 0;

            try
            {
                using (FileStream sourceFs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.Read))
                using (FileStream destFs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                using (BinaryReader br = new BinaryReader(sourceFs))
                using (BinaryWriter bw = new BinaryWriter(destFs))
                {
                    // 1. Сначала копируем заголовок (первые 28 байт)
                    sourceFs.Seek(0, SeekOrigin.Begin);

                    byte[] signature = br.ReadBytes(2);
                    ushort recordLen = br.ReadUInt16();
                    int oldP_FirstRec = br.ReadInt32(); // читаем, но не используем
                    int oldP_FreeSpace = br.ReadInt32(); // читаем, но не используем
                    byte[] nameSpec = br.ReadBytes(16);

                    // Записываем заголовок в новый файл
                    bw.Write(signature);
                    bw.Write(recordLen);
                    bw.Write(-1); // временно p_FirstRec
                    bw.Write(0);  // временно p_FreeSpace
                    bw.Write(nameSpec);

                    // 2. Теперь обрабатываем записи
                    int currentOffset = p_FirstRec;

                    while (currentOffset != -1 && currentOffset < sourceFs.Length)
                    {
                        sourceFs.Seek(currentOffset, SeekOrigin.Begin);

                        byte flag = br.ReadByte();
                        int firstComp = br.ReadInt32();
                        int nextOffset = br.ReadInt32();

                        ushort nameLen = br.ReadUInt16();
                        byte[] nameBytes = br.ReadBytes(nameLen);

                        // Если запись не удалена - сохраняем
                        if (flag != 0xFF)
                        {
                            long recordStart = destFs.Position;

                            if (newFirstRec == -1)
                                newFirstRec = (int)recordStart;

                            // Записываем запись (p_Next пока временный)
                            bw.Write(flag);
                            bw.Write(firstComp);
                            bw.Write(0); // временный p_Next
                            bw.Write(nameLen);
                            bw.Write(nameBytes);

                            // Обновляем ссылку предыдущей записи
                            if (lastValidOffset != -1)
                            {
                                long currentPos = destFs.Position;
                                destFs.Seek(lastValidOffset + 5, SeekOrigin.Begin); // +5 (flag + firstComp)
                                bw.Write((int)recordStart);
                                destFs.Seek(currentPos, SeekOrigin.Begin);
                            }

                            lastValidOffset = (int)recordStart;
                        }
                        else
                        {
                            removedCount++;
                        }

                        currentOffset = nextOffset;
                    }

                    // Закрываем список последней записи
                    if (lastValidOffset != -1)
                    {
                        destFs.Seek(lastValidOffset + 5, SeekOrigin.Begin);
                        bw.Write(-1);
                    }

                    // 3. Обновляем заголовок с правильными значениями
                    destFs.Seek(4, SeekOrigin.Begin); // позиция p_FirstRec
                    bw.Write(newFirstRec);

                    destFs.Seek(8, SeekOrigin.Begin); // позиция p_FreeSpace
                    bw.Write(0); // после компактизации свободного места нет
                }

                // Обновляем поля класса
                p_FirstRec = newFirstRec;
                p_FreeSpace = 0;

                // Заменяем файл
                File.Delete(CurrentFileName);
                File.Move(tempFile, CurrentFileName);

                Console.WriteLine($"Файл сжат. Удалено записей: {removedCount}");
            }
            catch
            {
                File.Delete(tempFile);
                throw;
            }
        }


        public void PrintDev()
        {
            if (!IsOpen)
                throw new Exception("Файл не открыт");

            try
            {
                using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    if (p_FirstRec == -1)
                    {
                        Console.WriteLine("Записей нет.");
                        return;
                    }

                    int offset = p_FirstRec;

                    while (offset != -1 && offset < fs.Length)
                    {
                        fs.Seek(offset, SeekOrigin.Begin);

                        RecordPRD record = new RecordPRD
                        {
                            FlagDelete = br.ReadByte(),
                            p_FirstComp = br.ReadInt32(),
                            p_Next = br.ReadInt32()
                        };

                        ushort nameLen = br.ReadUInt16();
                        byte[] nameBytes = br.ReadBytes(nameLen);
                        record.Name = Encoding.UTF8.GetString(nameBytes);

                        string type = record.IsDetail ? "Деталь" : record.IsAssembly ? "Узел/Изделие" : "Неизвестно";
                        string deleted = record.IsDeleted ? " (удален)" : "";

                        Console.WriteLine($"Offset: {offset} | {type}{deleted} | FirstComp: {record.p_FirstComp} | Next: {record.p_Next} | Name: {record.Name}");

                        offset = record.p_Next;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Ошибка чтения: " + ex.Message);
            }
        }
        public void Print(string name)
        {
            if (name == "*")
            {
                PrintAll();
                return;
            }
            List<RecordPRD> allRecords = new List<RecordPRD>();
            Dictionary<int, RecordPRD> recordsByOffset = new Dictionary<int, RecordPRD>();
            int foundOffset = -1;
            RecordPRD foundRecord = null;

            using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader br = new BinaryReader(fs))
            {
                int currentOffset = p_FirstRec;
                while (currentOffset != -1 && currentOffset < fs.Length)
                {
                    fs.Seek(currentOffset, SeekOrigin.Begin);

                    RecordPRD record = new RecordPRD
                    {
                        FlagDelete = br.ReadByte(),
                        p_FirstComp = br.ReadInt32(),
                        p_Next = br.ReadInt32()
                    };

                    string type = record.IsDetail ? "Деталь" : record.IsAssembly ? "Узел/Изделие" : "Неизвестно";

                    ushort nameLen = br.ReadUInt16();
                    byte[] nameBytes = br.ReadBytes(nameLen);
                    record.Name = Encoding.UTF8.GetString(nameBytes);

                    recordsByOffset[currentOffset] = record;
                    allRecords.Add(record);

                    if (record.Name == name)
                    {
                        if (type == "Деталь") throw new Exception($"Компонент '{name}' явлется деталью!");
                        foundOffset = currentOffset;
                        foundRecord = record;
                        break;
                    }

                    currentOffset = record.p_Next;
                }

                if (foundOffset == -1) throw new Exception($"Компонент '{name}' не найден");

                Console.WriteLine(name);
            }

        }

        private void PrintAll()
        {
            try
            {
                using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    if (p_FirstRec == -1)
                    {
                        Console.WriteLine("Записей нет.");
                        return;
                    }

                    int offset = p_FirstRec;

                    //Console.WriteLine($"Наименование; Тип");
                    while (offset != -1 && offset < fs.Length)
                    {
                        fs.Seek(offset, SeekOrigin.Begin);

                        RecordPRD record = new RecordPRD
                        {
                            FlagDelete = br.ReadByte(),
                            p_FirstComp = br.ReadInt32(),
                            p_Next = br.ReadInt32()
                        };

                        ushort nameLen = br.ReadUInt16();
                        byte[] nameBytes = br.ReadBytes(nameLen);
                        record.Name = Encoding.UTF8.GetString(nameBytes);

                        string type = record.IsDetail ? "Деталь" : record.IsAssembly ? "Узел/Изделие" : "Неизвестно";
                        string deleted = record.IsDeleted ? " (удален)" : "";

                        Console.WriteLine($"Наименование: {record.Name}; Тип: {type}");
                        //Console.WriteLine($"{record.Name}; {type}");

                        offset = record.p_Next;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Ошибка чтения: " + ex.Message);
            }

        }
    }

    public class RecordPRD
    {
        public byte FlagDelete { get; set; }
        public int p_FirstComp { get; set; }
        public int p_Next { get; set; }
        public string Name { get; set; }

        public bool IsDeleted => FlagDelete == 0xFF;
        public bool IsDetail => p_FirstComp == -1;
        public bool IsAssembly => p_FirstComp != -1;
    }

    internal class PRDFile
    {
    }
}