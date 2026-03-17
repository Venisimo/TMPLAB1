using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

enum Type { PRODUCT, ASSEMBLY, DETAIL, UNKNOWN };

namespace TMPLAB1
{
    public class PRD: IFile
    {
        public byte[] NameSpec { get; set; } = new byte[16];
        public bool IsOpen { get; set; }
        public string CurrentFileName { get; set; }

        public HeaderPRD Header { get; set; } = new HeaderPRD();

        public IFileHeader FileHeader
        {
            get => Header;
            set => Header = (HeaderPRD)value;
        }

        public RecordPRD Record { get; set; } = new RecordPRD();

        IRecord IFile.Record
        {
            get => Record;
            set => Record = (RecordPRD)value;
        }

        public PRD(string fileName)
        {
            CurrentFileName = fileName;
        }
        public PRD(string fileName, string recLen)
        {
            CurrentFileName = fileName;
            Header.RecordLen = ushort.Parse(recLen);
            Header.p_FirstRecord = -1;
            Header.p_FreeSpace = 0;
        }

        private (RecordPRD, string) ReadRecord(BinaryReader br)
        {
            RecordPRD read = new RecordPRD(
                            br.ReadByte(),
                            br.ReadInt32(),
                            br.ReadInt32(),
                            br.ReadBytes(Header.RecordLen)
                        );

            string recordName = Encoding.UTF8.GetString(read.Name).TrimEnd('\0');
            return (read, recordName);
        }

        public void Create()
        {
            string pureName = Path.GetFileNameWithoutExtension(CurrentFileName);
            string prsName = pureName + ".prs";

            Console.WriteLine(prsName);

            if (File.Exists(CurrentFileName))
            {
                Console.WriteLine($"Файл {CurrentFileName} уже существует!");

                try
                {
                    using (BinaryReader br = new BinaryReader(File.OpenRead(CurrentFileName)))
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

            Header.NameSpec = Encoding.ASCII.GetBytes(prsName.PadRight(16));

            using (BinaryWriter bw = new BinaryWriter(File.Create(CurrentFileName)))
            {
                bw.Write(Header.Signature);
                bw.Write(Header.RecordLen);
                bw.Write(Header.p_FirstRecord);
                bw.Write(Header.p_FreeSpace);
                bw.Write(Header.NameSpec);      
            }

            Console.WriteLine($"Файл {CurrentFileName} создан.");

            PRS prsFile = new PRS(prsName);
            prsFile.Create();

            IsOpen = true;
            Console.WriteLine($"Файл {CurrentFileName} открыт для работы.");
        }

        public void Open()
        {
            if (!File.Exists(CurrentFileName)) throw new Exception($"Файла {CurrentFileName} не существует");

            try
            {
                using (BinaryReader br = new BinaryReader(File.OpenRead(CurrentFileName)))
                {
                    Header.Signature = br.ReadBytes(2);
                    string signatureStr = Encoding.ASCII.GetString(Header.Signature);
                    
                    if (signatureStr != "PS") throw new Exception("Неверная сигнатура файла");

                    Header.RecordLen = br.ReadUInt16();
                    Header.p_FirstRecord = br.ReadInt32();
                    Header.p_FreeSpace = br.ReadInt32();
                    Header.NameSpec = br.ReadBytes(16);

                    if (NameSpec.Length < 16) throw new Exception("Файл поврежден: неполный заголовок");
                }

                IsOpen = true;
                Console.WriteLine($"Файл {CurrentFileName} открыт");
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

            string name = parts[0];
            string typeStr = parts[1];
            Type type = GetComponentType(typeStr);

            if (type == Type.UNKNOWN) throw new Exception("Неизвестный тип компонента");

            if (name.Length > Header.RecordLen) throw new Exception("Превышена максимальная длина имени");

            byte[] nameBytes = Encoding.UTF8.GetBytes(name);

            RecordPRD newRecord = new RecordPRD(0, -1, Header.p_FirstRecord, nameBytes);

            using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader br = new BinaryReader(fs))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                int currentOffset = Header.p_FirstRecord;
                while (currentOffset != -1 && currentOffset < fs.Length)
                {
                    fs.Seek(currentOffset, SeekOrigin.Begin);

                    (RecordPRD read, string nameStr) = ReadRecord(br);

                    if (nameStr == name && !read.IsDeleted)
                    {
                        throw new Exception($"Компонент с именем '{name}' уже существует!");
                    }
                    currentOffset = read.p_Next;
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
                byte[] nameBuffer = new byte[Header.RecordLen];
                Array.Copy(nameBytes, nameBuffer, nameBytes.Length);

                bw.Write(nameBuffer);
                bw.Flush();

                fs.Seek(4, SeekOrigin.Begin);
                bw.Write(newOffset);

                fs.Seek(8, SeekOrigin.Begin);
                int newFreeSpace = oldP_FreeSpace + 1 + 4 + 4 + Header.RecordLen;
                bw.Write(newFreeSpace);

                Header.p_FirstRecord = newOffset;
                Header.p_FreeSpace = newFreeSpace;
            }

            Console.WriteLine($"Компонент '{name}' ({typeStr}) добавлен.");
        }

        private void CheckRelate(int foundOffset, PRS filePRS)
        {
            using (FileStream prsStream = new(filePRS.CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader prsReader = new BinaryReader(prsStream))
            using (BinaryWriter prsWriter = new BinaryWriter(prsStream))
            {
                prsStream.Seek(0, SeekOrigin.Begin);

                filePRS.Header.p_FirstRecord = prsReader.ReadInt32();

                int currentOffset = filePRS.Header.p_FirstRecord;

                while (currentOffset != -1)
                {
                    prsStream.Seek(currentOffset, SeekOrigin.Begin);

                    filePRS.Record.FlagDelete = prsReader.ReadByte();
                    filePRS.Record.p_Product = prsReader.ReadInt32();
                    filePRS.Record.p_Detail = prsReader.ReadInt32();

                    if (filePRS.Record.p_Product == foundOffset || filePRS.Record.p_Detail == foundOffset) 
                        throw new Exception("На компонент имеются ссылки в спецификациях других компонент");

                    filePRS.Record.MultiOccurrence = prsReader.ReadUInt16();
                    filePRS.Record.p_Next = prsReader.ReadInt32();

                    currentOffset = filePRS.Record.p_Next;
                }
            }
        }

        public void Delete(string name)
        {
            if (!IsOpen) throw new Exception("Файл не открыт");

            if (string.IsNullOrEmpty(name)) throw new Exception("Укажите имя компонента для удаления");

            int foundOffset = -1;

            string NameSpec = Encoding.UTF8.GetString(Header.NameSpec);

            PRS filePRS = new PRS();
            filePRS.CurrentFileName = NameSpec;

            using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader br = new BinaryReader(fs))
            {
                int currentOffset = Header.p_FirstRecord;
                while (currentOffset != -1 && currentOffset < fs.Length)
                {
                    fs.Seek(currentOffset, SeekOrigin.Begin);

                    (RecordPRD read, string nameStr) = ReadRecord(br);

                    if (nameStr == name && !read.IsDeleted)
                    {
                        foundOffset = currentOffset;
                        break;
                    }
                    currentOffset = read.p_Next;  
                }
            }

            CheckRelate(foundOffset, filePRS);

            if (foundOffset == -1) throw new Exception($"Компонент '{name}' не найден");

            using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                fs.Seek(foundOffset, SeekOrigin.Begin);
                bw.Write((byte)0xFF);
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
                int currentOffset = Header.p_FirstRecord;
                int foundOffset = -1;

                while (currentOffset != -1 && currentOffset < fs.Length)
                {
                    fs.Seek(currentOffset, SeekOrigin.Begin);

                    (RecordPRD read, string nameStr) = ReadRecord(br);

                    if (nameStr == name)
                    {
                        if (!read.IsDeleted)
                            throw new Exception($"Компонент '{name}' не удален");

                        foundOffset = currentOffset;
                        break;
                    }

                    currentOffset = read.p_Next;
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
                int currentOffset = Header.p_FirstRecord;
                int restoredCount = 0;

                while (currentOffset != -1 && currentOffset < fs.Length)
                {
                    fs.Seek(currentOffset, SeekOrigin.Begin);

                    (RecordPRD read, string nameStr) = ReadRecord(br);

                    if (!read.IsDeleted)
                    {
                        fs.Seek(currentOffset, SeekOrigin.Begin);
                        bw.Write((byte)0x00);
                        restoredCount++;
                    }

                    currentOffset = read.p_Next;
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

            // Сохраняем маппинг старых offset -> новые для PRS
            Dictionary<int, int> offsetMapForPrs = new Dictionary<int, int>();

            try
            {
                using (FileStream sourceFs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.Read))
                using (FileStream destFs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                using (BinaryReader br = new BinaryReader(sourceFs))
                using (BinaryWriter bw = new BinaryWriter(destFs))
                {
                    sourceFs.Seek(0, SeekOrigin.Begin);

                    byte[] signature = br.ReadBytes(2);
                    ushort recordLen = br.ReadUInt16();
                    int oldP_FirstRec = br.ReadInt32();
                    int oldP_FreeSpace = br.ReadInt32();
                    byte[] nameSpec = br.ReadBytes(16);

                    bw.Write(signature);
                    bw.Write(recordLen);
                    bw.Write(-1);  // временный p_FirstRecord
                    bw.Write(0);
                    bw.Write(nameSpec);

                    int currentOffset = Header.p_FirstRecord;

                    // ЭТАП 1: Первый проход - запомнить маппинг offset'ов
                    List<(int oldOffset, long newOffset, RecordPRD record, string name)> liveRecords = new();

                    while (currentOffset != -1 && currentOffset < sourceFs.Length)
                    {
                        sourceFs.Seek(currentOffset, SeekOrigin.Begin);
                        (RecordPRD read, string nameStr) = ReadRecord(br);

                        if (!read.IsDeleted)
                        {
                            long recordStart = destFs.Position;
                            offsetMapForPrs[currentOffset] = (int)recordStart;

                            bw.Write(read.FlagDelete);
                            bw.Write(read.p_FirstComp);
                            bw.Write(0); // временный p_Next
                            bw.Write(read.Name);

                            liveRecords.Add((currentOffset, recordStart, read, nameStr));

                            if (newFirstRec == -1)
                                newFirstRec = (int)recordStart;

                            lastValidOffset = (int)recordStart;
                        }
                        else
                        {
                            removedCount++;
                        }

                        currentOffset = read.p_Next;
                    }

                    // ЭТАП 2: Второй проход - обновить p_Next указатели
                    for (int i = 0; i < liveRecords.Count; i++)
                    {
                        long recordPos = liveRecords[i].newOffset;
                        int nextPointer = (i < liveRecords.Count - 1) ? (int)liveRecords[i + 1].newOffset : -1;

                        destFs.Seek(recordPos + 5, SeekOrigin.Begin); // +5 = flag(1) + p_FirstComp(4)
                        bw.Write(nextPointer);
                    }

                    // ЭТАП 3: Обновить заголовок
                    destFs.Seek(4, SeekOrigin.Begin);
                    bw.Write(newFirstRec);
                    destFs.Seek(8, SeekOrigin.Begin);
                    bw.Write(0);
                }

                Header.p_FirstRecord = newFirstRec;
                Header.p_FreeSpace = 0;

                File.Delete(CurrentFileName);
                File.Move(tempFile, CurrentFileName);

                // ЭТАП 4: Синхронизировать PRS
                string prsFileName = Encoding.UTF8.GetString(Header.NameSpec).Trim();
                if (File.Exists(prsFileName))
                {
                    UpdatePrsAfterPrdTruncate(prsFileName, offsetMapForPrs);
                }

                Console.WriteLine($"Файл сжат. Удалено записей: {removedCount}");
            }
            catch
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
                throw;
            }
        }

        /// <summary>
        /// Обновляет PRS файл после truncate PRD
        /// Переиндексирует ссылки на PRD (p_Product и p_Detail)
        /// ВАЖНО: Закрывает все потоки перед изменением файла!
        /// </summary>
        private void UpdatePrsAfterPrdTruncate(string prsFileName, Dictionary<int, int> prdOffsetMap)
        {
            string tempFile = Path.GetTempFileName();

            try
            {
                using (FileStream sourceFs = new FileStream(prsFileName, FileMode.Open, FileAccess.Read))
                using (FileStream destFs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                using (BinaryReader br = new BinaryReader(sourceFs))
                using (BinaryWriter bw = new BinaryWriter(destFs))
                {
                    sourceFs.Seek(0, SeekOrigin.Begin);
                    int oldFirstRecord = br.ReadInt32();
                    int oldFreeSpace = br.ReadInt32();

                    bw.Write(-1); // временный p_FirstRecord
                    bw.Write(0);

                    int currentOffset = oldFirstRecord;
                    int newFirstRecord = -1;
                    List<(int oldOffset, long newOffset, int p_Product, int p_Detail, ushort multiOcc)> liveRecords = new();

                    // ЭТАП 1: Первый проход
                    while (currentOffset != -1 && currentOffset < sourceFs.Length)
                    {
                        sourceFs.Seek(currentOffset, SeekOrigin.Begin);

                        byte flagDelete = br.ReadByte();
                        int p_Product = br.ReadInt32();
                        int p_Detail = br.ReadInt32();
                        ushort multiOccurrence = br.ReadUInt16();
                        int p_Next = br.ReadInt32();

                        if (flagDelete != 0xFF) // Живая запись
                        {
                            long recordPos = destFs.Position;

                            // Обновляем ссылки на PRD если они есть в маппинге
                            if (prdOffsetMap.ContainsKey(p_Product))
                                p_Product = prdOffsetMap[p_Product];

                            if (prdOffsetMap.ContainsKey(p_Detail))
                                p_Detail = prdOffsetMap[p_Detail];

                            bw.Write(flagDelete);
                            bw.Write(p_Product);
                            bw.Write(p_Detail);
                            bw.Write(multiOccurrence);
                            bw.Write(0); // временный p_Next

                            liveRecords.Add((currentOffset, recordPos, p_Product, p_Detail, multiOccurrence));

                            if (newFirstRecord == -1)
                                newFirstRecord = (int)recordPos;
                        }

                        currentOffset = p_Next;
                    }

                    // ЭТАП 2: Второй проход - обновить p_Next
                    for (int i = 0; i < liveRecords.Count; i++)
                    {
                        long recordPos = liveRecords[i].newOffset;
                        int nextPointer = (i < liveRecords.Count - 1) ? (int)liveRecords[i + 1].newOffset : -1;

                        destFs.Seek(recordPos + 11, SeekOrigin.Begin); // +11 = flag(1) + p_Product(4) + p_Detail(4) + multiOcc(2)
                        bw.Write(nextPointer);
                    }

                    // ЭТАП 3: Обновить заголовок
                    destFs.Seek(0, SeekOrigin.Begin);
                    bw.Write(newFirstRecord);
                    bw.Write(0);
                } // <-- ВСЕ ПОТОКИ ЗАКРЫТЫ ЗДЕСЬ!

                // Теперь файлы закрыты, можем их ме��ять
                File.Delete(prsFileName);
                File.Move(tempFile, prsFileName);
            }
            catch
            {
                if (File.Exists(tempFile))
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
                    if (Header.p_FirstRecord == -1)
                    {
                        Console.WriteLine("Записей нет.");
                        return;
                    }

                    int offset = Header.p_FirstRecord;

                    string NameSpec = Encoding.UTF8.GetString(Header.NameSpec);

                    Console.WriteLine($"HEADER: p_FirstRecord {Header.p_FirstRecord} | NameSpec: {NameSpec} | FreeSpace: {Header.p_FreeSpace} | RecordLen: {Header.RecordLen}");

                    while (offset != -1 && offset < fs.Length)
                    {
                        fs.Seek(offset, SeekOrigin.Begin);

                        (RecordPRD read, string recordName) = ReadRecord(br);

                        string type = read.IsDetail ? "Деталь" : read.IsAssembly ? "Узел/Изделие" : "Неизвестно";
                        string deleted = read.IsDeleted ? " (удален)" : "";

                        Console.WriteLine($"Offset: {offset} | {type}{deleted} | FirstComp: {read.p_FirstComp} | Next: {read.p_Next} | Name: {recordName}");

                        offset = read.p_Next;
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

            string NameSpec = Encoding.UTF8.GetString(Header.NameSpec).Trim();

            PRS filePRS = new PRS();
            filePRS.CurrentFileName = NameSpec;

            using (FileStream prsStream = new(filePRS.CurrentFileName, FileMode.Open, FileAccess.Read))
            using (BinaryReader prsReader = new(prsStream))
            using (FileStream prdStream = new(CurrentFileName, FileMode.Open, FileAccess.Read))
            using (BinaryReader prdReader = new(prdStream))
            {
                int currentOffset = Header.p_FirstRecord;
                int firstComp = 0;

                while (currentOffset != -1 && currentOffset < prdStream.Length)
                {
                    prdStream.Seek(currentOffset, SeekOrigin.Begin);
                    (RecordPRD read, string nameStr) = ReadRecord(prdReader);
                    string type = read.IsDetail ? "Деталь" : read.IsAssembly ? "Узел/Изделие" : "Неизвестно";
                    firstComp = read.p_FirstComp;

                    if (nameStr == name)
                    {
                        if (type == "Деталь")
                            throw new Exception($"Компонент '{name}' является деталью!");
                        break;
                    }
                    currentOffset = read.p_Next;
                }

                if (currentOffset == -1)
                    throw new Exception($"Компонент не найден!");

                Console.WriteLine();
                Console.WriteLine(name);

                // === Читаем ВСЕ связи из PRS и строим карту ===
                var childrenMap = BuildChildrenMap(prsStream, prdStream, prsReader, prdReader);

                // === Выводим дерево ===
                if (childrenMap.ContainsKey(name))
                {
                    DrawTreeFromMap(childrenMap, name, "");
                }
                Console.WriteLine();
            }
        }
        private Dictionary<string, List<(string childName, bool isAssembly, string childFirstCompRef)>> BuildChildrenMap(
            FileStream prsStream, FileStream prdStream, BinaryReader prsReader, BinaryReader prdReader)
        {
            var childrenMap = new Dictionary<string, List<(string, bool, string)>>();

            prsStream.Seek(0, SeekOrigin.Begin);
            int firstRecord = prsReader.ReadInt32();
            int freeSpace = prsReader.ReadInt32();

            int offset = firstRecord;

            while (offset != -1 && offset < prsStream.Length)
            {
                prsStream.Seek(offset, SeekOrigin.Begin);

                byte flagDelete = prsReader.ReadByte();
                int p_Product = prsReader.ReadInt32();
                int p_Detail = prsReader.ReadInt32();
                ushort multiOcc = prsReader.ReadUInt16();
                int p_Next = prsReader.ReadInt32();

                // Пропускаем удаленные записи
                if (flagDelete == 0xFF)
                {
                    offset = p_Next;
                    continue;
                }

                // Читаем имя продукта (родителя)
                prdStream.Seek(p_Product, SeekOrigin.Begin);
                (RecordPRD prodRec, string prodName) = ReadRecord(prdReader);

                // Читаем имя детали и проверяем, является ли она узлом
                prdStream.Seek(p_Detail, SeekOrigin.Begin);
                (RecordPRD detailRec, string detailName) = ReadRecord(prdReader);

                bool isAssembly = detailRec.IsAssembly;
                string detailRefStr = detailRec.p_FirstComp.ToString();

                // Добавляем в карту
                if (!childrenMap.ContainsKey(prodName))
                    childrenMap[prodName] = new List<(string, bool, string)>();

                childrenMap[prodName].Add((detailName, isAssembly, detailRefStr));

                offset = p_Next;
            }

            return childrenMap;
        }

        private void DrawTreeFromMap(
            Dictionary<string, List<(string childName, bool isAssembly, string childRef)>> childrenMap,
            string parentName, string prefix)
        {
            if (!childrenMap.ContainsKey(parentName))
                return;

            var children = childrenMap[parentName];

            for (int i = 0; i < children.Count; i++)
            {
                var (childName, isAssembly, _) = children[i];
                bool isLast = (i == children.Count - 1);

                // Выводим текущий элемент
                Console.Write(prefix);
                Console.Write(isLast ? "└── " : "├── ");
                Console.WriteLine(childName);

                // Если это узел/изделие, рекурсивно выводим его детей
                if (isAssembly)
                {
                    string newPrefix = prefix + (isLast ? "    " : "│   ");
                    DrawTreeFromMap(childrenMap, childName, newPrefix);
                }
            }
        }


        private void PrintAll()
        {
            try
            {
                using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    if (Header.p_FirstRecord == -1)
                    {
                        Console.WriteLine("Записей нет.");
                        return;
                    }

                    int offset = Header.p_FirstRecord;

                    //Console.WriteLine($"Наименование; Тип");
                    while (offset != -1 && offset < fs.Length)
                    {
                        fs.Seek(offset, SeekOrigin.Begin);

                        (RecordPRD read, string nameStr) = ReadRecord(br);

                        string type = read.IsDetail ? "Деталь" : read.IsAssembly ? "Узел/Изделие" : "Неизвестно";

                        if (!read.IsDeleted) Console.WriteLine($"Наименование: {nameStr}; Тип: {type}");
                        //Console.WriteLine($"{record.Name}; {type}");

                        offset = read.p_Next;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Ошибка чтения: " + ex.Message);
            }

        }
    }
}