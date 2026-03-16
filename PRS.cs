using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Reflection.PortableExecutable;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace TMPLAB1
{
    public class PRS : IFile
    {
        public bool IsOpen { get; set; }
        public string CurrentFileName { get; set; }

        public HeaderPRS Header { get; set; } = new HeaderPRS();

        public IFileHeader FileHeader
        {
            get => Header;
            set => Header = (HeaderPRS)value;
        }

        public RecordPRS Record { get; set; } = new RecordPRS();

        IRecord IFile.Record
        {
            get => Record;
            set => Record = (RecordPRS)value;
        }

        public PRS()
        { 
        
        }

        public PRS(string fileName)
        { 
            CurrentFileName = fileName;
            Header.p_FirstRecord = -1;
            Header.p_FreeSpace = 0;
        }

        public void Create()
        {
            using (BinaryWriter bw = new BinaryWriter(File.Create(CurrentFileName)))
            {
                bw.Write(Header.p_FirstRecord);
                bw.Write(Header.p_FreeSpace);
                Console.WriteLine($"Файл {CurrentFileName} создан.");
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
                    fs.Seek(0, SeekOrigin.Begin);

                    Header.p_FirstRecord = br.ReadInt32();
                    Header.p_FreeSpace = br.ReadInt32();

                    Console.WriteLine($"=== PRS HEADER ===");
                    Console.WriteLine($"FirstRecord: {Header.p_FirstRecord} (0x{Header.p_FirstRecord:X})");
                    Console.WriteLine($"FreeSpace: {Header.p_FreeSpace} bytes");
                    Console.WriteLine();

                    // Открываем PRD для получения имен компонентов
                    string prdFileName = Path.ChangeExtension(CurrentFileName, ".prd");
                    Dictionary<int, string> componentNames = new Dictionary<int, string>();

                    if (File.Exists(prdFileName))
                    {
                        using (FileStream prdStream = new FileStream(prdFileName, FileMode.Open, FileAccess.Read))
                        using (BinaryReader prdReader = new BinaryReader(prdStream))
                        {
                            prdStream.Seek(2, SeekOrigin.Begin);
                            ushort recordLen = prdReader.ReadUInt16();
                            int prdFirstRecord = prdReader.ReadInt32();

                            // Собираем все имена компонентов
                            int compOffset = prdFirstRecord;
                            while (compOffset != -1 && compOffset < prdStream.Length)
                            {
                                prdStream.Seek(compOffset, SeekOrigin.Begin);

                                byte flag = prdReader.ReadByte();
                                int p_FirstComp = prdReader.ReadInt32();
                                int p_Next = prdReader.ReadInt32();
                                byte[] nameBytes = prdReader.ReadBytes(recordLen);
                                string name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

                                if (flag != 0xFF)
                                {
                                    componentNames[compOffset] = name;
                                }

                                compOffset = p_Next;
                            }
                        }
                    }

                    // Находим все записи в файле
                    List<int> allOffsets = new List<int>();
                    long fileLength = fs.Length;
                    int currentOffset = 8; // Первая запись начинается после заголовка (8 байт)

                    while (currentOffset + 11 <= fileLength)
                    {
                        allOffsets.Add(currentOffset);
                        currentOffset += 11;
                    }

                    if (allOffsets.Count == 0)
                    {
                        Console.WriteLine("Записей нет.");
                        return;
                    }

                    Console.WriteLine("=== PRS RECORDS ===");
                    Console.WriteLine($"{"Offset",-8} {"Flag",-4} {"Component",-15} {"Mult",-6} {"Next",-8} {"Status"}");
                    Console.WriteLine(new string('-', 65));

                    int recordCount = 0;
                    foreach (int offset in allOffsets)
                    {
                        fs.Seek(offset, SeekOrigin.Begin);

                        byte flag = br.ReadByte();
                        int p_Component = br.ReadInt32();
                        ushort mult = br.ReadUInt16();
                        int p_Next = br.ReadInt32();

                        string status = flag == 0xFF ? "DELETED" : "ACTIVE";
                        string flagStr = flag == 0xFF ? "FF" : "00";

                        // Получаем имя компонента
                        string componentName = componentNames.ContainsKey(p_Component)
                            ? componentNames[p_Component]
                            : $"0x{p_Component:X}";

                        // Получаем имя следующей записи (для отладки)
                        string nextName = "";
                        if (p_Next != -1 && componentNames.ContainsKey(p_Next))
                        {
                            nextName = $" → {componentNames[p_Next]}";
                        }

                        Console.WriteLine($"{offset,-8:X} {flagStr,-4} {componentName,-15} {mult,-6} {p_Next,-8:X}{nextName} [{status}]");
                        recordCount++;
                    }

                    Console.WriteLine(new string('-', 65));
                    Console.WriteLine($"Всего записей: {recordCount}");

                    Console.WriteLine("\n=== СПИСКИ СПЕЦИФИКАЦИЙ ===");

                    // Проходим по всем компонентам PRD и показываем их спецификации
                    foreach (var kvp in componentNames)
                    {
                        int compOffset = kvp.Key;
                        string compName = kvp.Value;

                        // Получаем p_FirstComp из PRD
                        using (FileStream prdStream = new FileStream(prdFileName, FileMode.Open, FileAccess.Read))
                        using (BinaryReader prdReader = new BinaryReader(prdStream))
                        {
                            prdStream.Seek(2, SeekOrigin.Begin);
                            ushort recordLen = prdReader.ReadUInt16();
                            int prdFirstRecord = prdReader.ReadInt32();

                            // Ищем этот компонент
                            int searchOffset = prdFirstRecord;
                            while (searchOffset != -1 && searchOffset < prdStream.Length)
                            {
                                prdStream.Seek(searchOffset, SeekOrigin.Begin);

                                byte flag = prdReader.ReadByte();
                                int p_FirstComp = prdReader.ReadInt32();
                                int p_Next = prdReader.ReadInt32();
                                byte[] nameBytes = prdReader.ReadBytes(recordLen);
                                string name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

                                if (flag != 0xFF && name == compName)
                                {
                                    if (p_FirstComp != -1)
                                    {
                                        Console.WriteLine($"\n{compName} (спецификация начинается с offset {p_FirstComp:X}):");

                                        // Проходим по всем записям спецификации
                                        int specOffset = p_FirstComp;
                                        while (specOffset != -1)
                                        {
                                            fs.Seek(specOffset, SeekOrigin.Begin);

                                            byte specFlag = br.ReadByte();
                                            int specComp = br.ReadInt32();
                                            ushort specMult = br.ReadUInt16();
                                            int specNext = br.ReadInt32();

                                            if (specFlag != 0xFF)
                                            {
                                                string childName = componentNames.ContainsKey(specComp)
                                                    ? componentNames[specComp]
                                                    : $"0x{specComp:X}";

                                                Console.WriteLine($"  -> {childName} (x{specMult})" +
                                                    (specNext != -1 ? "" : " [последняя]"));
                                            }

                                            specOffset = specNext;
                                        }
                                    }
                                    break;
                                }

                                searchOffset = p_Next;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Ошибка чтения PRS: " + ex.Message);
            }
        }

        public void Open()
        {
            if (!File.Exists(CurrentFileName)) throw new Exception($"Файла {CurrentFileName} не существует");

            try
            {
                IsOpen = true;
                Console.WriteLine($"Файл {CurrentFileName} открыт");
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка при открытии файла: {ex.Message}");
            }
        }

        private (RecordPRD, string) ReadRecord(BinaryReader br, ushort RecordLen)
        {
            RecordPRD read = new RecordPRD(
                            br.ReadByte(),
                            br.ReadInt32(),
                            br.ReadInt32(),
                            br.ReadBytes(RecordLen)
                        );

            string recordName = Encoding.UTF8.GetString(read.Name).TrimEnd('\0');
            return (read, recordName);
        }

        private string ReadString(BinaryReader reader)
        {
            ushort length = reader.ReadUInt16();
            byte[] bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }
        private void WriteString(BinaryWriter writer, byte[] bytes)
        {
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        private int FindComponent(FileStream stream, BinaryReader reader, int firstRecord, string name, ushort RecordLen)
        {
            int offset = firstRecord;

            while (offset != -1 && offset < stream.Length)
            {
                stream.Seek(offset, SeekOrigin.Begin);
                (RecordPRD read, string nameStr) = ReadRecord(reader, RecordLen);

                if (nameStr == name) return offset;

                offset = read.p_Next;
            }

            return -1;
        }

        private int FindComponentInPRD(FileStream prdStream, BinaryReader prdReader,
            int firstRecord, string name, ushort recordLen)
        {
            int offset = firstRecord;

            while (offset != -1 && offset < prdStream.Length)
            {
                prdStream.Seek(offset, SeekOrigin.Begin);

                byte flagDelete = prdReader.ReadByte();
                int p_FirstComp = prdReader.ReadInt32();
                int p_Next = prdReader.ReadInt32();
                byte[] nameBytes = prdReader.ReadBytes(recordLen);
                string currentName = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

                Console.WriteLine($"Поиск: ищем '{name}', нашли '{currentName}' на offset {offset}"); // Отладка

                if (flagDelete != 0xFF && currentName == name)
                    return offset;

                offset = p_Next;
            }

            return -1;
        }

        public void Input(string argument)
        {
            string[] parts = argument
                .Replace("(", "")
                .Replace(")", "")
                .Replace("/", " ")
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
                throw new Exception("Формат: input (Родитель/Потомок)");

            string parentName = parts[0];
            string childName = parts[1];

            string prdFileName = Path.ChangeExtension(CurrentFileName, ".prd");
            if (!File.Exists(prdFileName))
                throw new Exception($"Файл {prdFileName} не существует");

            using (FileStream prsStream = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader prsReader = new BinaryReader(prsStream))
            using (BinaryWriter prsWriter = new BinaryWriter(prsStream))
            using (FileStream prdStream = new FileStream(prdFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader prdReader = new BinaryReader(prdStream))
            using (BinaryWriter prdWriter = new BinaryWriter(prdStream))
            {
                // Читаем заголовок PRD
                prdStream.Seek(2, SeekOrigin.Begin);
                ushort recordLen = prdReader.ReadUInt16();
                int prdFirstRecord = prdReader.ReadInt32();

                // Читаем заголовок PRS
                prsStream.Seek(0, SeekOrigin.Begin);
                int oldFirstRecord = prsReader.ReadInt32();
                int oldFreeSpace = prsReader.ReadInt32();

                Header.p_FirstRecord = oldFirstRecord;
                Header.p_FreeSpace = oldFreeSpace;

                // Находим компоненты в PRD
                int parentOffset = FindComponentInPRD(prdStream, prdReader, prdFirstRecord, parentName, recordLen);
                if (parentOffset == -1)
                    throw new Exception($"Родительский компонент '{parentName}' не найден");

                int childOffset = FindComponentInPRD(prdStream, prdReader, prdFirstRecord, childName, recordLen);
                if (childOffset == -1)
                    throw new Exception($"Дочерний компонент '{childName}' не найден");

                // Получаем первую запись спецификации родителя
                prdStream.Seek(parentOffset, SeekOrigin.Begin);
                prdReader.ReadByte(); // flag
                int parentFirstComp = prdReader.ReadInt32();

                // Ищем существующую связь
                int currentSpecOffset = parentFirstComp;
                int lastSpecOffset = -1;
                bool relationExists = false;
                ushort currentMultiplicity = 0;

                while (currentSpecOffset != -1 && currentSpecOffset < prsStream.Length)
                {
                    prsStream.Seek(currentSpecOffset, SeekOrigin.Begin);

                    byte flag = prsReader.ReadByte();
                    int comp = prsReader.ReadInt32();
                    ushort mult = prsReader.ReadUInt16();
                    int next = prsReader.ReadInt32();

                    if (flag != 0xFF && comp == childOffset)
                    {
                        relationExists = true;
                        currentMultiplicity = (ushort)(mult + 1);

                        prsStream.Seek(currentSpecOffset + 5, SeekOrigin.Begin);
                        prsWriter.Write(currentMultiplicity);

                        Console.WriteLine($"Кратность вхождения увеличена: {parentName} -> {childName} (теперь {currentMultiplicity})");
                        break;
                    }

                    lastSpecOffset = currentSpecOffset;
                    currentSpecOffset = next;
                }

                if (!relationExists)
                {
                    // Создаем новую запись в PRS
                    prsStream.Seek(0, SeekOrigin.End);
                    int newRecordOffset = (int)prsStream.Position;

                    prsWriter.Write((byte)0);              // FlagDelete
                    prsWriter.Write(childOffset);           // p_Component
                    prsWriter.Write((ushort)1);             // MultiOccurrence
                    prsWriter.Write(-1);                     // p_Next

                    // Обновляем заголовок PRS
                    prsStream.Seek(0, SeekOrigin.Begin);
                    if (oldFirstRecord == -1)
                        prsWriter.Write(newRecordOffset);
                    else
                        prsWriter.Write(oldFirstRecord);

                    prsWriter.Write(oldFreeSpace + RecordPRS.RECORD_SIZE);

                    // Обновляем указатель в родительском компоненте PRD
                    if (parentFirstComp == -1)
                    {
                        prdStream.Seek(parentOffset + 1, SeekOrigin.Begin);
                        prdWriter.Write(newRecordOffset);
                        Console.WriteLine($"Первая спецификация для {parentName}");
                    }
                    else
                    {
                        // Находим последнюю запись в списке
                        int lastOffset = parentFirstComp;
                        int nextOffset = parentFirstComp;

                        while (nextOffset != -1)
                        {
                            lastOffset = nextOffset;
                            prsStream.Seek(nextOffset, SeekOrigin.Begin);
                            prsReader.ReadByte();
                            prsReader.ReadInt32();
                            prsReader.ReadUInt16();
                            nextOffset = prsReader.ReadInt32();
                        }

                        prsStream.Seek(lastOffset + 7, SeekOrigin.Begin);
                        prsWriter.Write(newRecordOffset);
                    }

                    Console.WriteLine($"Добавлена связь: {parentName} -> {childName} (кратность: 1)");
                }

                // Обновляем свойства в памяти
                prsStream.Seek(0, SeekOrigin.Begin);
                Header.p_FirstRecord = prsReader.ReadInt32();
                Header.p_FreeSpace = prsReader.ReadInt32();
            }
        }

        public void Print(string argument)
        {
            if (!IsOpen) throw new Exception("Файл не открыт");

            string prdFileName = Path.ChangeExtension(CurrentFileName, ".prd");

            if (!File.Exists(prdFileName))
            {
                Console.WriteLine($"Файл {prdFileName} не найден");
                return;
            }

            using (FileStream prsStream = new FileStream(CurrentFileName, FileMode.Open, FileAccess.Read))
            using (BinaryReader prsReader = new BinaryReader(prsStream))
            using (FileStream prdStream = new FileStream(prdFileName, FileMode.Open, FileAccess.Read))
            using (BinaryReader prdReader = new BinaryReader(prdStream))
            {
                // Читаем заголовок PRD
                prdStream.Seek(2, SeekOrigin.Begin);
                ushort recordLen = prdReader.ReadUInt16();
                int prdFirstRecord = prdReader.ReadInt32();

                // Словарь для имен компонентов
                Dictionary<int, string> componentNames = new Dictionary<int, string>();

                // Собираем все имена компонентов из PRD
                int compOffset = prdFirstRecord;
                while (compOffset != -1 && compOffset < prdStream.Length)
                {
                    prdStream.Seek(compOffset, SeekOrigin.Begin);

                    byte flag = prdReader.ReadByte();
                    int p_FirstComp = prdReader.ReadInt32();
                    int p_Next = prdReader.ReadInt32();
                    byte[] nameBytes = prdReader.ReadBytes(recordLen);
                    string name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

                    if (flag != 0xFF)
                    {
                        componentNames[compOffset] = name;
                    }

                    compOffset = p_Next;
                }

                // Если аргумент "*" - показываем все спецификации
                if (argument == "*")
                {
                    Console.WriteLine("\n=== ВСЕ СПЕЦИФИКАЦИИ ===\n");

                    // Для каждого компонента, у которого есть спецификация
                    compOffset = prdFirstRecord;
                    while (compOffset != -1 && compOffset < prdStream.Length)
                    {
                        prdStream.Seek(compOffset, SeekOrigin.Begin);

                        byte flag = prdReader.ReadByte();
                        int p_FirstComp = prdReader.ReadInt32();
                        int p_Next = prdReader.ReadInt32();
                        byte[] nameBytes = prdReader.ReadBytes(recordLen);
                        string name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

                        if (flag != 0xFF && p_FirstComp != -1)
                        {
                            Console.WriteLine($"\n{name}:");

                            int specOffset = p_FirstComp;
                            int itemNum = 1;

                            while (specOffset != -1 && specOffset < prsStream.Length)
                            {
                                prsStream.Seek(specOffset, SeekOrigin.Begin);

                                byte specFlag = prsReader.ReadByte();
                                int p_Component = prsReader.ReadInt32();
                                ushort mult = prsReader.ReadUInt16();
                                int specNext = prsReader.ReadInt32();

                                if (specFlag != 0xFF)
                                {
                                    string childName = componentNames.ContainsKey(p_Component)
                                        ? componentNames[p_Component]
                                        : $"0x{p_Component:X}";

                                    Console.WriteLine($"  {itemNum}. {childName} (x{mult})");
                                    itemNum++;
                                }

                                specOffset = specNext;
                            }
                        }

                        compOffset = p_Next;
                    }

                    return;
                }

                // Иначе - показываем спецификацию конкретного компонента
                string componentName = argument;

                // Находим компонент в PRD
                int componentOffset = -1;
                int searchOffset = prdFirstRecord;

                while (searchOffset != -1 && searchOffset < prdStream.Length)
                {
                    prdStream.Seek(searchOffset, SeekOrigin.Begin);

                    byte flag = prdReader.ReadByte();
                    int p_FirstComp = prdReader.ReadInt32();
                    int p_Next = prdReader.ReadInt32();
                    byte[] nameBytes = prdReader.ReadBytes(recordLen);
                    string name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

                    if (flag != 0xFF && name == componentName)
                    {
                        componentOffset = searchOffset;
                        break;
                    }

                    searchOffset = p_Next;
                }

                if (componentOffset == -1)
                {
                    Console.WriteLine($"Компонент '{componentName}' не найден");
                    return;
                }

                // Получаем первую запись спецификации
                prdStream.Seek(componentOffset + 1, SeekOrigin.Begin);
                int firstSpecOffset = prdReader.ReadInt32();

                if (firstSpecOffset == -1)
                {
                    Console.WriteLine($"Компонент '{componentName}' является деталью и не имеет спецификации");
                    return;
                }

                // Выводим спецификацию
                Console.WriteLine($"\nСпецификация компонента '{componentName}':");
                Console.WriteLine(new string('=', 40));

                int currentSpecOffset = firstSpecOffset;
                int itemNumber = 1;

                while (currentSpecOffset != -1 && currentSpecOffset < prsStream.Length)
                {
                    prsStream.Seek(currentSpecOffset, SeekOrigin.Begin);

                    byte flag = prsReader.ReadByte();
                    int p_Component = prsReader.ReadInt32();
                    ushort mult = prsReader.ReadUInt16();
                    int p_Next = prsReader.ReadInt32();

                    if (flag != 0xFF)
                    {
                        string childName = componentNames.ContainsKey(p_Component)
                            ? componentNames[p_Component]
                            : $"0x{p_Component:X}";

                        Console.WriteLine($"{itemNumber}. {childName} (x{mult})");
                        itemNumber++;
                    }

                    currentSpecOffset = p_Next;
                }

                if (itemNumber == 1)
                {
                    Console.WriteLine("Спецификация пуста");
                }
            }
        }

        public void Restore(string name)
        {
            if (!IsOpen) throw new Exception("Файл не открыт");

            if (string.IsNullOrEmpty(name)) throw new Exception("Укажите связь для восстановления");

            if (name == "*")
            {
                RestoreAll();
                return;
            }

            string[] parts = name
                .Replace("(", "")
                .Replace(")", "")
                .Replace("/", " ")
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
                throw new Exception("Формат: restore (Родитель/Потомок)");

            string parentName = parts[0];
            string childName = parts[1];

            string prdFileName = Path.ChangeExtension(CurrentFileName, ".prd");

            using (FileStream prsStream = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader prsReader = new BinaryReader(prsStream))
            using (BinaryWriter prsWriter = new BinaryWriter(prsStream))
            using (FileStream prdStream = new FileStream(prdFileName, FileMode.Open, FileAccess.Read))
            using (BinaryReader prdReader = new BinaryReader(prdStream))
            {
                // Читаем заголовки
                prdStream.Seek(2, SeekOrigin.Begin);
                ushort recordLen = prdReader.ReadUInt16();
                int prdFirstRecord = prdReader.ReadInt32();

                prsStream.Seek(0, SeekOrigin.Begin);
                Header.p_FirstRecord = prsReader.ReadInt32();

                // Находим родителя и потомка
                int parentOffset = FindComponentInPRD(prdStream, prdReader, prdFirstRecord, parentName, recordLen);
                if (parentOffset == -1)
                    throw new Exception($"Родительский компонент '{parentName}' не найден");

                int childOffset = FindComponentInPRD(prdStream, prdReader, prdFirstRecord, childName, recordLen);
                if (childOffset == -1)
                    throw new Exception($"Дочерний компонент '{childName}' не найден");

                // Ищем удаленную связь
                prdStream.Seek(parentOffset, SeekOrigin.Begin);
                prdReader.ReadByte(); // flag
                int parentFirstComp = prdReader.ReadInt32();

                int currentOffset = parentFirstComp;
                bool found = false;

                while (currentOffset != -1 && currentOffset < prsStream.Length)
                {
                    prsStream.Seek(currentOffset, SeekOrigin.Begin);

                    byte flag = prsReader.ReadByte();
                    int p_Component = prsReader.ReadInt32();
                    ushort mult = prsReader.ReadUInt16();
                    int p_Next = prsReader.ReadInt32();

                    if (flag == 0xFF && p_Component == childOffset)
                    {
                        found = true;

                        // Восстанавливаем запись
                        prsStream.Seek(currentOffset, SeekOrigin.Begin);
                        prsWriter.Write((byte)0x00);
                        Console.WriteLine($"Связь {parentName} -> {childName} восстановлена");
                        break;
                    }

                    currentOffset = p_Next;
                }

                if (!found)
                    throw new Exception($"Удаленная связь {parentName} -> {childName} не найдена");
            }
        }

        private void RestoreAll()
        {
            using (FileStream prsStream = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader prsReader = new BinaryReader(prsStream))
            using (BinaryWriter prsWriter = new BinaryWriter(prsStream))
            {
                // Читаем все записи в файле
                List<int> allOffsets = new List<int>();
                long fileLength = prsStream.Length;
                int currentOffset = 8;

                while (currentOffset + 11 <= fileLength)
                {
                    allOffsets.Add(currentOffset);
                    currentOffset += 11;
                }

                int restoredCount = 0;

                // Проходим по ВСЕМ записям, не только по главному списку
                foreach (int offset in allOffsets)
                {
                    prsStream.Seek(offset, SeekOrigin.Begin);

                    byte flag = prsReader.ReadByte();
                    int p_Component = prsReader.ReadInt32();
                    ushort mult = prsReader.ReadUInt16();
                    int p_Next = prsReader.ReadInt32();

                    if (flag == 0xFF) // Если запись удалена
                    {
                        // Восстанавливаем
                        prsStream.Seek(offset, SeekOrigin.Begin);
                        prsWriter.Write((byte)0x00);
                        restoredCount++;

                        Console.WriteLine($"Восстановлена запись на offset {offset:X} (компонент 0x{p_Component:X})");
                    }
                }

                Console.WriteLine($"Восстановлено связей: {restoredCount}");
            }
        }

        public void Truncate()
        {
            if (!IsOpen) throw new Exception("Файл не открыт");

            string tempFile = Path.GetTempFileName();
            string prdFileName = Path.ChangeExtension(CurrentFileName, ".prd");

            // Словарь для хранения новых offset'ов старых записей
            Dictionary<int, int> oldToNewOffset = new Dictionary<int, int>();

            try
            {
                // ШАГ 1: Собираем все активные записи из PRS
                List<PRSRecord> activeRecords = new List<PRSRecord>();

                using (FileStream source = new FileStream(CurrentFileName, FileMode.Open, FileAccess.Read))
                using (BinaryReader br = new BinaryReader(source))
                {
                    long fileLength = source.Length;
                    int currentOffset = 8; // Первая запись после заголовка (8 байт)

                    while (currentOffset + 11 <= fileLength) // 11 = размер записи
                    {
                        source.Seek(currentOffset, SeekOrigin.Begin);

                        byte flag = br.ReadByte();
                        int p_Component = br.ReadInt32();
                        ushort mult = br.ReadUInt16();
                        int p_Next = br.ReadInt32();

                        if (flag != 0xFF) // Только активные записи
                        {
                            activeRecords.Add(new PRSRecord
                            {
                                OldOffset = currentOffset,
                                Flag = flag,
                                Component = p_Component,
                                Multiplicity = mult,
                                Next = p_Next
                            });
                        }

                        currentOffset += 11;
                    }
                }

                // ШАГ 2: Записываем активные записи в новый файл
                using (FileStream dest = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                using (BinaryWriter bw = new BinaryWriter(dest))
                {
                    // Временный заголовок
                    bw.Write(-1);
                    bw.Write(0);

                    // Сначала записываем все записи, чтобы узнать их новые offset'ы
                    foreach (var record in activeRecords)
                    {
                        int newOffset = (int)dest.Position;
                        oldToNewOffset[record.OldOffset] = newOffset;

                        bw.Write(record.Flag);
                        bw.Write(record.Component);
                        bw.Write(record.Multiplicity);
                        bw.Write(record.Next); // Пока пишем старый p_Next
                    }

                    // ШАГ 3: Обновляем p_Next в новом файле
                    for (int i = 0; i < activeRecords.Count; i++)
                    {
                        var record = activeRecords[i];
                        int newOffset = oldToNewOffset[record.OldOffset];

                        // Если p_Next не -1 И существует в словаре
                        if (record.Next != -1 && oldToNewOffset.ContainsKey(record.Next))
                        {
                            // Обновляем p_Next на новый offset
                            dest.Seek(newOffset + 7, SeekOrigin.Begin); // +1(flag) +4(component) +2(mult)
                            bw.Write(oldToNewOffset[record.Next]);
                        }
                        else if (record.Next != -1)
                        {
                            // Ссылка на несуществующую запись (удаленную) - обнуляем
                            dest.Seek(newOffset + 7, SeekOrigin.Begin);
                            bw.Write(-1);
                        }
                        // Если record.Next == -1, оставляем как есть
                    }

                    // ШАГ 4: Обновляем заголовок - первую запись
                    dest.Seek(0, SeekOrigin.Begin);
                    if (Header.p_FirstRecord != -1 && oldToNewOffset.ContainsKey(Header.p_FirstRecord))
                    {
                        bw.Write(oldToNewOffset[Header.p_FirstRecord]);
                    }
                    else
                    {
                        bw.Write(-1);
                    }
                    bw.Write(0); // p_FreeSpace
                }

                // ШАГ 5: Заменяем файл
                File.Delete(CurrentFileName);
                File.Move(tempFile, CurrentFileName);

                // ШАГ 6: Обновляем p_FirstComp в PRD
                using (FileStream prdStream = new FileStream(prdFileName, FileMode.Open, FileAccess.ReadWrite))
                using (BinaryReader prdReader = new BinaryReader(prdStream))
                using (BinaryWriter prdWriter = new BinaryWriter(prdStream))
                {
                    prdStream.Seek(2, SeekOrigin.Begin);
                    ushort recordLen = prdReader.ReadUInt16();
                    int prdFirstRecord = prdReader.ReadInt32();

                    int compOffset = prdFirstRecord;
                    while (compOffset != -1 && compOffset < prdStream.Length)
                    {
                        prdStream.Seek(compOffset, SeekOrigin.Begin);

                        byte flag = prdReader.ReadByte();
                        int p_FirstComp = prdReader.ReadInt32();
                        int p_Next = prdReader.ReadInt32();

                        if (flag != 0xFF && p_FirstComp != -1)
                        {
                            if (oldToNewOffset.ContainsKey(p_FirstComp))
                            {
                                // Обновляем p_FirstComp на новый offset
                                prdStream.Seek(compOffset + 1, SeekOrigin.Begin);
                                prdWriter.Write(oldToNewOffset[p_FirstComp]);
                                Console.WriteLine($"Обновлена ссылка для компонента на offset {compOffset:X}: {p_FirstComp:X} -> {oldToNewOffset[p_FirstComp]:X}");
                            }
                            else
                            {
                                // Ссылка вела на удаленную запись - обнуляем
                                prdStream.Seek(compOffset + 1, SeekOrigin.Begin);
                                prdWriter.Write(-1);
                                Console.WriteLine($"Ссылка на удаленную запись обнулена для компонента на offset {compOffset:X}");
                            }
                        }

                        compOffset = p_Next;
                    }
                }

                // Обновляем заголовок в памяти
                if (Header.p_FirstRecord != -1 && oldToNewOffset.ContainsKey(Header.p_FirstRecord))
                {
                    Header.p_FirstRecord = oldToNewOffset[Header.p_FirstRecord];
                }
                else
                {
                    Header.p_FirstRecord = -1;
                }
                Header.p_FreeSpace = 0;

                Console.WriteLine($"Файл PRS сжат. Удалено записей: {activeRecords.Count}");
            }
            catch (Exception ex)
            {
                File.Delete(tempFile);
                throw new Exception($"Ошибка при сжатии PRS: {ex.Message}");
            }
        }

        // Вспомогательный класс
        private class PRSRecord
        {
            public int OldOffset { get; set; }
            public byte Flag { get; set; }
            public int Component { get; set; }
            public ushort Multiplicity { get; set; }
            public int Next { get; set; }
        }

        public void Delete(string name)
        {
            if (!IsOpen) throw new Exception("Файл не открыт");

            // Для PRS файла команда delete ожидает формат "Родитель/Потомок"
            // Но интерфейс требует просто string name
            // Поэтому парсим строку внутри метода

            string[] parts = name
                .Replace("(", "")
                .Replace(")", "")
                .Replace("/", " ")
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
                throw new Exception("Формат: delete (Родитель/Потомок)");

            string parentName = parts[0];
            string childName = parts[1];

            string prdFileName = Path.ChangeExtension(CurrentFileName, ".prd");

            using (FileStream prsStream = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader prsReader = new BinaryReader(prsStream))
            using (BinaryWriter prsWriter = new BinaryWriter(prsStream))
            using (FileStream prdStream = new FileStream(prdFileName, FileMode.Open, FileAccess.Read))
            using (BinaryReader prdReader = new BinaryReader(prdStream))
            {
                // Читаем заголовки
                prdStream.Seek(2, SeekOrigin.Begin);
                ushort recordLen = prdReader.ReadUInt16();
                int prdFirstRecord = prdReader.ReadInt32();

                prsStream.Seek(0, SeekOrigin.Begin);
                Header.p_FirstRecord = prsReader.ReadInt32();

                // Находим родителя и потомка
                int parentOffset = FindComponentInPRD(prdStream, prdReader, prdFirstRecord, parentName, recordLen);
                if (parentOffset == -1)
                    throw new Exception($"Родительский компонент '{parentName}' не найден");

                int childOffset = FindComponentInPRD(prdStream, prdReader, prdFirstRecord, childName, recordLen);
                if (childOffset == -1)
                    throw new Exception($"Дочерний компонент '{childName}' не найден");

                // Ищем связь в спецификации родителя
                prdStream.Seek(parentOffset, SeekOrigin.Begin);
                prdReader.ReadByte(); // flag
                int parentFirstComp = prdReader.ReadInt32();

                int currentOffset = parentFirstComp;
                int prevOffset = -1;
                bool found = false;

                while (currentOffset != -1 && currentOffset < prsStream.Length)
                {
                    prsStream.Seek(currentOffset, SeekOrigin.Begin);

                    byte flag = prsReader.ReadByte();
                    int p_Component = prsReader.ReadInt32();
                    ushort mult = prsReader.ReadUInt16();
                    int p_Next = prsReader.ReadInt32();

                    if (flag != 0xFF && p_Component == childOffset)
                    {
                        found = true;

                        if (mult > 1)
                        {
                            // Уменьшаем кратность
                            ushort newMult = (ushort)(mult - 1);
                            prsStream.Seek(currentOffset + 5, SeekOrigin.Begin); // +1(flag) +4(p_Component)
                            prsWriter.Write(newMult);
                            Console.WriteLine($"Кратность вхождения уменьшена: {parentName} -> {childName} (теперь {newMult})");
                        }
                        else
                        {
                            // Помечаем запись на удаление
                            prsStream.Seek(currentOffset, SeekOrigin.Begin);
                            prsWriter.Write((byte)0xFF);
                            Console.WriteLine($"Связь {parentName} -> {childName} помечена на удаление");
                        }
                        break;
                    }

                    prevOffset = currentOffset;
                    currentOffset = p_Next;
                }

                if (!found)
                    throw new Exception($"Связь {parentName} -> {childName} не найдена");
            }
        }

    }
}
