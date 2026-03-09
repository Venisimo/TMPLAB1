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
        public void Delete(string name) { }
        public void Restore(string name) { }
        public void Truncate() { }

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

                Console.WriteLine(nameStr);

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

            if (firstComp == -1)
            {
                prdStream.Seek(mainRecordOffset + 1, SeekOrigin.Begin);
                prdWriter.Write(newRecordOffset);
            }

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
    }
}
