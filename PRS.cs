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

                    Console.WriteLine($"HEADER | FirstRecord: {Header.p_FirstRecord} | FreeSpace: {Header.p_FreeSpace}");

                    if (Header.p_FirstRecord == -1)
                    {
                        Console.WriteLine("Записей нет.");
                        return;
                    }

                    int offset = Header.p_FirstRecord;

                    while (offset != -1 && offset < fs.Length)
                    {
                        fs.Seek(offset, SeekOrigin.Begin);

                        RecordPRS record = new RecordPRS
                        {
                            FlagDelete = br.ReadByte(),
                            p_Product = br.ReadInt32(),
                            p_Detail = br.ReadInt32(),
                            MultiOccurrence = br.ReadUInt16(),
                            p_Next = br.ReadInt32()
                        };

                        string deleted = record.IsDeleted ? " (удален)" : "";

                        Console.WriteLine(
                            $"Offset: {offset} | Product: {record.p_Product} | Detail: {record.p_Detail} | MultiOccurrence: {record.MultiOccurrence}{deleted} | Next: {record.p_Next}"
                        );

                        offset = record.p_Next;
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

        public void Input(string argument)
        {
            string[] parts = argument
                .Replace("(", "")
                .Replace(")", "")
                .Replace(",", "")
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
                throw new Exception("Формат: input <Компонент> <Деталь>");

            string mainComponent = parts[0];
            string detailComponent = parts[1];

            string prdFileName = Path.ChangeExtension(CurrentFileName, ".prd");
            PRD filePRD = new PRD(prdFileName);

            using FileStream prsStream = new(CurrentFileName, FileMode.Open, FileAccess.ReadWrite);
            using BinaryReader prsReader = new(prsStream);
            using BinaryWriter prsWriter = new(prsStream);

            using FileStream prdStream = new(prdFileName, FileMode.Open, FileAccess.ReadWrite);
            using BinaryReader prdReader = new(prdStream);
            using BinaryWriter prdWriter = new(prdStream);

            // ===== HEADER PRS =====
            prsStream.Seek(0, SeekOrigin.Begin);
            Header.p_FirstRecord = prsReader.ReadInt32();
            Header.p_FreeSpace = prsReader.ReadInt32();

            // ===== HEADER PRD =====
            prdStream.Seek(2, SeekOrigin.Begin);
            filePRD.Header.RecordLen = prdReader.ReadUInt16();
            filePRD.Header.p_FirstRecord = prdReader.ReadInt32();

            // ===== Поиск компонентов =====
            int mainRecordOffset = FindComponent(prdStream, prdReader,
                filePRD.Header.p_FirstRecord, mainComponent, filePRD.Header.RecordLen);

            int detailRecordOffset = FindComponent(prdStream, prdReader,
                filePRD.Header.p_FirstRecord, detailComponent, filePRD.Header.RecordLen);

            if (mainRecordOffset == -1)
                throw new Exception($"Компонент '{mainComponent}' отсутствует в PRD");

            if (detailRecordOffset == -1)
                throw new Exception($"Компонент '{detailComponent}' отсутствует в PRD");

            // ===== Проверка дубликата =====
            int currentOffset = Header.p_FirstRecord;

            ushort MultiOccurrence = 1;

            while (currentOffset != -1 && currentOffset + 15 <= prsStream.Length)
            {
                prsStream.Seek(currentOffset, SeekOrigin.Begin);

                Record.FlagDelete = prsReader.ReadByte();
                Record.p_Product = prsReader.ReadInt32();
                Record.p_Detail = prsReader.ReadInt32();
                Record.MultiOccurrence = prsReader.ReadUInt16();
                Record.p_Next = prsReader.ReadInt32();

                if (Record.p_Product == mainRecordOffset)
                {
                    if (!Record.IsDeleted && Record.p_Detail == detailRecordOffset)
                        throw new Exception($"Связь '{mainComponent} -> {detailComponent}' уже существует");

                    Record.MultiOccurrence++;

                    MultiOccurrence = Record.MultiOccurrence;

                    prsStream.Seek(currentOffset + 1 + 4 + 4, SeekOrigin.Begin);
                    prsWriter.Write(Record.MultiOccurrence);
                }

                currentOffset = Record.p_Next;
            }

            prsStream.Seek(0, SeekOrigin.End);
            int newRecordOffset = (int)prsStream.Position;

            Record.FlagDelete = 0;
            Record.p_Product = mainRecordOffset;
            Record.p_Detail = detailRecordOffset;
            Record.MultiOccurrence = MultiOccurrence;
            Record.p_Next = Header.p_FirstRecord;

            prsWriter.Write(Record.FlagDelete);
            prsWriter.Write(Record.p_Product);
            prsWriter.Write(Record.p_Detail);
            prsWriter.Write(Record.MultiOccurrence);
            prsWriter.Write(Record.p_Next);

            Header.p_FirstRecord = newRecordOffset;
            Header.p_FreeSpace += 15;

            prsStream.Seek(0, SeekOrigin.Begin);
            prsWriter.Write(Header.p_FirstRecord);
            prsWriter.Write(Header.p_FreeSpace);

            prdStream.Seek(mainRecordOffset + 1, SeekOrigin.Begin);
            int firstComp = prdReader.ReadInt32();

            prdStream.Seek(mainRecordOffset + 1, SeekOrigin.Begin);
            prdWriter.Write(newRecordOffset);

            Console.WriteLine($"Добавлена связь: {mainComponent} -> {detailComponent}");
        }

        public void Print(string argument)
        {
            using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.Read))
            using (BinaryReader br = new BinaryReader(fs))
            {
                fs.Seek(0, SeekOrigin.Begin);

                Header.p_FirstRecord = br.ReadInt32();
                Header.p_FreeSpace = br.ReadInt32();

                int currentOffset = Header.p_FirstRecord;

                if (currentOffset == -1)
                {
                    Console.WriteLine("Файл пуст.");
                    return;
                }

                while (currentOffset != -1)
                {
                    if (currentOffset < 8 || currentOffset >= fs.Length)
                        throw new Exception("Файл поврежден");

                    fs.Seek(currentOffset, SeekOrigin.Begin);

                    byte flagDelete = br.ReadByte();
                    int p_FirstComp = br.ReadInt32();
                    int p_Next = br.ReadInt32();

                    ushort mainLen = br.ReadUInt16();
                    byte[] mainBytes = br.ReadBytes(mainLen);
                    string main = Encoding.UTF8.GetString(mainBytes);

                    ushort detailLen = br.ReadUInt16();
                    byte[] detailBytes = br.ReadBytes(detailLen);
                    string detail = Encoding.UTF8.GetString(detailBytes);

                    if (flagDelete == 0)
                    {
                        if (argument == "*" || argument == main)
                        {
                            Console.WriteLine($"{main} -> {detail}");
                        }
                    }

                    currentOffset = p_Next;
                }
            }
        }

        private void ChangeOccurance(int productOffset, int currentOffset, BinaryReader prsReader, BinaryWriter prsWriter, FileStream prsStream, bool isIncrease)
        {
            while (currentOffset != -1)
            {
                prsStream.Seek(currentOffset, SeekOrigin.Begin);

                Record.FlagDelete = prsReader.ReadByte();
                Record.p_Product = prsReader.ReadInt32();
                Record.p_Detail = prsReader.ReadInt32();

                Record.MultiOccurrence = prsReader.ReadUInt16();

                if (productOffset == Record.p_Product)
                {
                    ushort newValue = isIncrease
                        ? (ushort)(Record.MultiOccurrence + 1)
                        : (ushort)(Record.MultiOccurrence - 1);

                    prsStream.Seek(-2, SeekOrigin.Current);
                    prsWriter.Write(newValue);
                }

                Record.p_Next = prsReader.ReadInt32();
                currentOffset = Record.p_Next;
            }
        }

        public void Delete(string argument)
        {
            if (!IsOpen) throw new Exception("Файл не открыт");

            string[] parts = argument
             .Replace("(", "")
             .Replace(")", "")
             .Replace(",", "")
             .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            string product = parts[0];
            string detail = parts[1];

            string prdFileName = Path.ChangeExtension(CurrentFileName, ".prd");
            PRD filePRD = new PRD(prdFileName);

            using (FileStream prsStream = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader prsReader = new BinaryReader(prsStream))
            using (BinaryWriter prsWriter = new BinaryWriter(prsStream))
            using (FileStream prdStream = new FileStream(prdFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader prdReader = new BinaryReader(prdStream))
            using (BinaryWriter prdWriter = new BinaryWriter(prdStream))
            {

                prsStream.Seek(0, SeekOrigin.Begin);

                Header.p_FirstRecord = prsReader.ReadInt32();

                int currentOffset = Header.p_FirstRecord;

                if (currentOffset == -1)
                {
                    Console.WriteLine("Файл пуст.");
                    return;
                }

                prdStream.Seek(2, SeekOrigin.Begin);
                filePRD.Header.RecordLen = prdReader.ReadUInt16();
                filePRD.Header.p_FirstRecord = prdReader.ReadInt32();
                
                int productOffset = FindComponent(prdStream, prdReader, filePRD.Header.p_FirstRecord, product, filePRD.Header.RecordLen);
                int detailOffset = FindComponent(prdStream, prdReader, filePRD.Header.p_FirstRecord, detail, filePRD.Header.RecordLen);
                
                if (productOffset == -1) throw new Exception("Указнного узла/изделия не существует!");
                if (detailOffset == -1) throw new Exception("Указнной детали не существует!");

                while (currentOffset != -1)
                {
                    prsStream.Seek(currentOffset, SeekOrigin.Begin);

                    Record.FlagDelete = prsReader.ReadByte();
                    Record.p_Product = prsReader.ReadInt32();
                    Record.p_Detail = prsReader.ReadInt32();
                    Record.MultiOccurrence = prsReader.ReadUInt16();
                    Record.p_Next = prsReader.ReadInt32();

                    if (!Record.IsDeleted && Record.p_Product == productOffset && Record.p_Detail == detailOffset)
                    {
                        prsStream.Seek(currentOffset, SeekOrigin.Begin);
                        prsWriter.Write((byte)0xFF);
                        ChangeOccurance(productOffset, Header.p_FirstRecord, prsReader, prsWriter, prsStream, false);
                        Console.WriteLine($" Связь {product} -> {detail} помечена на удаление");
                        break;
                    }
                    currentOffset = Record.p_Next;
                }

                if (currentOffset == -1) throw new Exception("Связи не существует!");
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
             .Replace(",", "")
             .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            string product = parts[0];
            string detail = parts[1];

            string prdFileName = Path.ChangeExtension(CurrentFileName, ".prd");
            PRD filePRD = new PRD(prdFileName);

            using (FileStream prsStream = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader prsReader = new BinaryReader(prsStream))
            using (BinaryWriter prsWriter = new BinaryWriter(prsStream))
            using (FileStream prdStream = new FileStream(prdFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader prdReader = new BinaryReader(prdStream))
            using (BinaryWriter prdWriter = new BinaryWriter(prdStream))
            {
                prsStream.Seek(0, SeekOrigin.Begin);

                Header.p_FirstRecord = prsReader.ReadInt32();

                int currentOffset = Header.p_FirstRecord;

                if (currentOffset == -1)
                {
                    Console.WriteLine("Файл пуст.");
                    return;
                }

                prdStream.Seek(2, SeekOrigin.Begin);
                filePRD.Header.RecordLen = prdReader.ReadUInt16();
                filePRD.Header.p_FirstRecord = prdReader.ReadInt32();

                int productOffset = FindComponent(prdStream, prdReader, filePRD.Header.p_FirstRecord, product, filePRD.Header.RecordLen);
                int detailOffset = FindComponent(prdStream, prdReader, filePRD.Header.p_FirstRecord, detail, filePRD.Header.RecordLen);

                if (productOffset == -1) throw new Exception("Указнного узла/изделия не существует!");
                if (detailOffset == -1) throw new Exception("Указнной детали не существует!");

                while (currentOffset != -1)
                {
                    prsStream.Seek(currentOffset, SeekOrigin.Begin);

                    Record.FlagDelete = prsReader.ReadByte();
                    Record.p_Product = prsReader.ReadInt32();
                    Record.p_Detail = prsReader.ReadInt32();
                    Record.MultiOccurrence = prsReader.ReadUInt16();
                    Record.p_Next = prsReader.ReadInt32();

                    if (Record.IsDeleted && Record.p_Product == productOffset && Record.p_Detail == detailOffset)
                    {
                        prsStream.Seek(currentOffset, SeekOrigin.Begin);
                        prsWriter.Write((byte)0x00);
                        ChangeOccurance(productOffset, Header.p_FirstRecord, prsReader, prsWriter, prsStream, true);
                        Console.WriteLine("Связь восстановлена");
                        break;
                    }
                    currentOffset = Record.p_Next;
                    
                }

                if (currentOffset == -1) throw new Exception("Связи не существует, либо она не почена на удаление!");
            }
        }

        private void RestoreAll() 
        {

            using (FileStream prsStream = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader prsReader = new BinaryReader(prsStream))
            using (BinaryWriter prsWriter = new BinaryWriter(prsStream))
            {
                prsStream.Seek(0, SeekOrigin.Begin);

                Header.p_FirstRecord = prsReader.ReadInt32();

                int currentOffset = Header.p_FirstRecord;

                if (currentOffset == -1)
                {
                    Console.WriteLine("Файл пуст.");
                    return;
                }

                int count = 0;

                while (currentOffset != -1)
                {
                    prsStream.Seek(currentOffset, SeekOrigin.Begin);

                    Record.FlagDelete = prsReader.ReadByte();
                    Record.p_Product = prsReader.ReadInt32();
                    Record.p_Detail = prsReader.ReadInt32();
                    Record.MultiOccurrence = prsReader.ReadUInt16();
                    Record.p_Next = prsReader.ReadInt32();

                    int nextOffset = Record.p_Next;

                    if (Record.IsDeleted)
                    {
                        prsStream.Seek(currentOffset, SeekOrigin.Begin);
                        prsWriter.Write((byte)0x00);
                        ChangeOccurance(Record.p_Product, Header.p_FirstRecord, prsReader, prsWriter, prsStream, true);
                        count++;
                    }

                    currentOffset = nextOffset;
                }

                Console.WriteLine($"Восстановлено связей: {count}");

            }
        }

        //private void pChange(PRD filePRD)
        //{
        //    using (FileStream prdStream = new FileStream(filePRD.CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
        //    using (BinaryReader prdReader = new BinaryReader(prdStream))
        //    using (BinaryWriter prdWriter = new BinaryWriter(prdStream))
        //    {
        //        prdStream.Seek(2, SeekOrigin.Begin);
        //        filePRD.Header.RecordLen = prdReader.ReadUInt16();
        //        filePRD.Header.p_FirstRecord = prdReader.ReadInt32();

        //        int currentOffset = Header.p_FirstRecord;

        //        while (currentOffset != -1)
        //        {
        //            prdStream.Seek(currentOffset, SeekOrigin.Begin);

        //            (RecordPRD recordPRD, string ProductName) = ReadRecord(prdReader, filePRD.Header.RecordLen);

        //            if (ProductName == )

        //            currentOffset = filePRD.Record.p_Next;
        //        }
        //    }
        //}

        public void Truncate()
        {
            if (!IsOpen)
                throw new Exception("Файл не открыт");

            string tempFile = Path.GetTempFileName();
            int newFirstRecord = -1;
            int removedCount = 0;

            // Маппинг старых offset -> новые для PRD
            Dictionary<int, int> offsetMapForPrd = new Dictionary<int, int>();

            string prdFileName = Path.ChangeExtension(CurrentFileName, ".prd");

            try
            {
                using FileStream source = new FileStream(CurrentFileName, FileMode.Open, FileAccess.Read);
                using FileStream dest = new FileStream(tempFile, FileMode.Create, FileAccess.Write);
                using BinaryReader br = new BinaryReader(source);
                using BinaryWriter bw = new BinaryWriter(dest);

                source.Seek(0, SeekOrigin.Begin);
                int firstRecord = br.ReadInt32();
                int freeSpace = br.ReadInt32();

                bw.Write(-1); // временный p_FirstRecord
                bw.Write(0);

                int currentOffset = firstRecord;
                List<(int oldOffset, long newOffset, int p_Product, int p_Detail, ushort multiOcc)> liveRecords = new();

                // ЭТАП 1: Первый проход
                while (currentOffset != -1 && currentOffset < source.Length)
                {
                    source.Seek(currentOffset, SeekOrigin.Begin);

                    Record.FlagDelete = br.ReadByte();
                    Record.p_Product = br.ReadInt32();
                    Record.p_Detail = br.ReadInt32();
                    Record.MultiOccurrence = br.ReadUInt16();
                    Record.p_Next = br.ReadInt32();

                    int nextOffset = Record.p_Next;

                    if (!Record.IsDeleted)
                    {
                        long recordPos = dest.Position;
                        offsetMapForPrd[currentOffset] = (int)recordPos;

                        bw.Write(Record.FlagDelete);
                        bw.Write(Record.p_Product);
                        bw.Write(Record.p_Detail);
                        bw.Write(Record.MultiOccurrence);
                        bw.Write(0); // временный p_Next

                        liveRecords.Add((currentOffset, recordPos, Record.p_Product, Record.p_Detail, Record.MultiOccurrence));

                        if (newFirstRecord == -1)
                            newFirstRecord = (int)recordPos;
                    }
                    else
                    {
                        removedCount++;
                    }

                    currentOffset = nextOffset;
                }

                // ЭТАП 2: Второй проход - обновить p_Next
                for (int i = 0; i < liveRecords.Count; i++)
                {
                    long recordPos = liveRecords[i].newOffset;
                    int nextPointer = (i < liveRecords.Count - 1) ? (int)liveRecords[i + 1].newOffset : -1;

                    dest.Seek(recordPos + 11, SeekOrigin.Begin);
                    bw.Write(nextPointer);
                }

                dest.Seek(0, SeekOrigin.Begin);
                bw.Write(newFirstRecord);
                bw.Write(0);
            } 
            catch (Exception e) { Console.Write($"Ошибка: {e.Message}"); }
    
            Header.p_FirstRecord = newFirstRecord;
            Header.p_FreeSpace = 0;

            File.Delete(CurrentFileName);
            File.Move(tempFile, CurrentFileName);

            // ЭТАП 4: Синхронизировать PRD
            if (File.Exists(prdFileName))
            {
                UpdatePrdAfterPrsTruncate(prdFileName, offsetMapForPrd);
            }

            Console.WriteLine($"Файл сжат. Удалено записей: {removedCount}");
        }

        /// <summary>
        /// Обновляет PRD файл после truncate PRS
        /// Переиндексирует ссылки на PRS (p_FirstComp)
        /// ВАЖНО: Закрывает все потоки перед изменением файла!
        /// </summary>
        private void UpdatePrdAfterPrsTruncate(string prdFileName, Dictionary<int, int> prsOffsetMap)
        {
            string tempFile = Path.GetTempFileName();

            try
            {
                using (FileStream sourceFs = new FileStream(prdFileName, FileMode.Open, FileAccess.Read))
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
                    bw.Write(-1);
                    bw.Write(0);
                    bw.Write(nameSpec);

                    int currentOffset = oldP_FirstRec;
                    int newFirstRec = -1;
                    List<(int oldOffset, long newOffset, int p_FirstComp)> liveRecords = new();

                    // ЭТАП 1: Первый проход
                    while (currentOffset != -1 && currentOffset < sourceFs.Length)
                    {
                        sourceFs.Seek(currentOffset, SeekOrigin.Begin);

                        byte flagDelete = br.ReadByte();
                        int p_FirstComp = br.ReadInt32();
                        int p_Next = br.ReadInt32();
                        byte[] name = br.ReadBytes(recordLen);

                        if (flagDelete != 0xFF) // Живая запись
                        {
                            long recordPos = destFs.Position;

                            // Обновляем ссылку на PRS если она есть в маппинге
                            if (prsOffsetMap.ContainsKey(p_FirstComp))
                                p_FirstComp = prsOffsetMap[p_FirstComp];

                            bw.Write(flagDelete);
                            bw.Write(p_FirstComp);
                            bw.Write(0); // временный p_Next
                            bw.Write(name);

                            liveRecords.Add((currentOffset, recordPos, p_FirstComp));

                            if (newFirstRec == -1)
                                newFirstRec = (int)recordPos;
                        }

                        currentOffset = p_Next;
                    }

                    // ЭТАП 2: Второй проход - обновить p_Next
                    for (int i = 0; i < liveRecords.Count; i++)
                    {
                        long recordPos = liveRecords[i].newOffset;
                        int nextPointer = (i < liveRecords.Count - 1) ? (int)liveRecords[i + 1].newOffset : -1;

                        destFs.Seek(recordPos + 5, SeekOrigin.Begin);
                        bw.Write(nextPointer);
                    }

                    // ЭТАП 3: Обновить заголовок
                    destFs.Seek(4, SeekOrigin.Begin);
                    bw.Write(newFirstRec);
                    destFs.Seek(8, SeekOrigin.Begin);
                    bw.Write(0);
                } // <-- ВСЕ ПОТОКИ ЗАКРЫТЫ ЗДЕСЬ!

                // Теперь файлы закрыты, можем их менять
                File.Delete(prdFileName);
                File.Move(tempFile, prdFileName);
            }
            catch
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
                throw;
            }
        }
    }
}
