using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

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

        public void Delete(string name)
        {
            if (!IsOpen) throw new Exception("Файл не открыт");

            if (string.IsNullOrEmpty(name)) throw new Exception("Укажите имя компонента для удаления");

            int foundOffset = -1;

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
                    bw.Write(-1);
                    bw.Write(0);
                    bw.Write(nameSpec);

                    int currentOffset = Header.p_FirstRecord;

                    while (currentOffset != -1 && currentOffset < sourceFs.Length)
                    {
                        sourceFs.Seek(currentOffset, SeekOrigin.Begin);

                        (RecordPRD read, string nameStr) = ReadRecord(br);

                        if (!read.IsDeleted)
                        {
                            long recordStart = destFs.Position;

                            if (newFirstRec == -1)
                                newFirstRec = (int)recordStart;

                            bw.Write(read.FlagDelete);
                            bw.Write(read.p_FirstComp);
                            bw.Write(0); // временный p_Next
                            bw.Write(read.Name);

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
                        else removedCount++;

                        currentOffset = read.p_Next;
                    }

                    if (lastValidOffset != -1)
                    {
                        destFs.Seek(lastValidOffset + 5, SeekOrigin.Begin);
                        bw.Write(-1);
                    }

                    destFs.Seek(4, SeekOrigin.Begin);
                    bw.Write(newFirstRec);

                    destFs.Seek(8, SeekOrigin.Begin);
                    bw.Write(0);
                }

                Header.p_FirstRecord = newFirstRec;
                Header.p_FreeSpace = 0;

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

            string NameSpec = Encoding.UTF8.GetString(Header.NameSpec);

            PRS filePRS = new PRS();
            filePRS.CurrentFileName = NameSpec;

            FileStream prsStream = new(filePRS.CurrentFileName, FileMode.Open, FileAccess.ReadWrite);
            BinaryReader prsReader = new(prsStream);

            FileStream prdStream = new(CurrentFileName, FileMode.Open, FileAccess.ReadWrite);
            BinaryReader prdReader = new(prdStream);


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
                    if (type == "Деталь") throw new Exception($"Компонент '{name}' явлется деталью!");
                    break;
                }

                currentOffset = read.p_Next;
            }

            Console.WriteLine(name);
            Console.WriteLine("|");
            
            prsStream.Seek(firstComp, SeekOrigin.Begin);

            filePRS.Record.FlagDelete = prsReader.ReadByte();
            filePRS.Record.p_Product = prsReader.ReadInt32();
            filePRS.Record.p_Detail = prsReader.ReadInt32();

            prdStream.Seek(filePRS.Record.p_Detail, SeekOrigin.Begin);

            Record.Name = prdReader.ReadBytes(Header.RecordLen);

            string NameDetail = Encoding.UTF8.GetString(Record.Name);

            Console.WriteLine(NameDetail);

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
                        string deleted = read.IsDeleted ? " (удален)" : "";

                        Console.WriteLine($"Наименование: {nameStr}; Тип: {type}");
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