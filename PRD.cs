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
    public class PRD : IFile
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
            byte flag = br.ReadByte();
            int p_FirstComp = br.ReadInt32();
            int p_Next = br.ReadInt32();
            byte[] nameBytes = br.ReadBytes(Header.RecordLen);

            RecordPRD read = new RecordPRD(flag, p_FirstComp, p_Next, nameBytes);
            string recordName = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

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

            if (argument.Contains("/"))
            {
                string prsFileName = Encoding.UTF8.GetString(Header.NameSpec).TrimEnd('\0');
                PRS prsFile = new PRS(prsFileName);
                prsFile.Open();
                prsFile.Input(argument);
                return;
            }

            string[] parts = argument
                .Replace("(", "")
                .Replace(")", "")
                .Replace(",", "")
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
                throw new Exception("Формат: Input (имя компонента, тип)");

            string name = parts[0];
            string typeStr = parts[1];
            Type type = GetComponentType(typeStr);

            if (type == Type.UNKNOWN) throw new Exception("Неизвестный тип компонента");

            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            if (nameBytes.Length > Header.RecordLen)
                throw new Exception($"Превышена максимальная длина имени. Максимум {Header.RecordLen} байт в UTF-8");

            byte[] nameBuffer = new byte[Header.RecordLen];
            Array.Copy(nameBytes, 0, nameBuffer, 0, nameBytes.Length);

            RecordPRD newRecord = new RecordPRD(0, -1, Header.p_FirstRecord, nameBuffer);

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

                fs.Seek(0, SeekOrigin.End);
                int newOffset = (int)fs.Position;

                bw.Write(newRecord.FlagDelete);
                bw.Write(newRecord.p_FirstComp);
                bw.Write(newRecord.p_Next);
                bw.Write(nameBuffer);
                bw.Flush();

                fs.Seek(4, SeekOrigin.Begin);
                bw.Write(newOffset);

                fs.Seek(8, SeekOrigin.Begin);
                int newFreeSpace = Header.p_FreeSpace + 1 + 4 + 4 + Header.RecordLen;
                bw.Write(newFreeSpace);

                Header.p_FirstRecord = newOffset;
                Header.p_FreeSpace = newFreeSpace;
            }

            Console.WriteLine($"Компонент '{name}' ({typeStr}) добавлен.");
        }

        private void CheckReferences(int componentOffset)
        {
            string prsFileName = Encoding.UTF8.GetString(Header.NameSpec).TrimEnd('\0');

            if (!File.Exists(prsFileName))
                return;

            using (FileStream prsStream = new FileStream(prsFileName, FileMode.Open, FileAccess.Read))
            using (BinaryReader prsReader = new BinaryReader(prsStream))
            {
                prsStream.Seek(0, SeekOrigin.Begin);
                int prsFirstRecord = prsReader.ReadInt32();

                int currentOffset = prsFirstRecord;

                while (currentOffset != -1 && currentOffset < prsStream.Length)
                {
                    prsStream.Seek(currentOffset, SeekOrigin.Begin);

                    byte flag = prsReader.ReadByte();
                    int p_Component = prsReader.ReadInt32();
                    ushort mult = prsReader.ReadUInt16();
                    int p_Next = prsReader.ReadInt32();

                    if (flag != 0xFF && p_Component == componentOffset)
                    {
                        throw new Exception($"Невозможно удалить компонент: на него есть ссылки в спецификациях");
                    }

                    currentOffset = p_Next;
                }
            }
        }

        public void Delete(string name)
        {
            if (!IsOpen) throw new Exception("Файл не открыт");

            if (string.IsNullOrEmpty(name)) throw new Exception("Укажите имя компонента для удаления");

            int foundOffset = -1;
            int prevOffset = -1;
            int nextOffset = -1;

            using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader br = new BinaryReader(fs))
            {
                int currentOffset = Header.p_FirstRecord;
                int previousOffset = -1;

                while (currentOffset != -1 && currentOffset < fs.Length)
                {
                    fs.Seek(currentOffset, SeekOrigin.Begin);

                    byte flag = br.ReadByte();
                    int p_FirstComp = br.ReadInt32();
                    int p_Next = br.ReadInt32();
                    byte[] nameBytes = br.ReadBytes(Header.RecordLen);
                    string nameStr = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

                    if (nameStr == name && flag != 0xFF)
                    {
                        foundOffset = currentOffset;
                        prevOffset = previousOffset;
                        nextOffset = p_Next;
                        break;
                    }

                    previousOffset = currentOffset;
                    currentOffset = p_Next;
                }
            }

            if (foundOffset == -1)
                throw new Exception($"Компонент '{name}' не найден");

            CheckReferences(foundOffset);

            using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                fs.Seek(foundOffset, SeekOrigin.Begin);
                bw.Write((byte)0xFF);

                Console.WriteLine($"Компонент '{name}' помечен как удаленный.");
            }
        }

        private void PrintComponentTree(int componentOffset, string componentName, int indent,
            FileStream prdStream, FileStream prsStream, BinaryReader prdReader, BinaryReader prsReader)
        {
            prdStream.Seek(componentOffset, SeekOrigin.Begin);

            byte flag = prdReader.ReadByte();
            int p_FirstComp = prdReader.ReadInt32();
            int p_Next = prdReader.ReadInt32();
            byte[] nameBytes = prdReader.ReadBytes(Header.RecordLen);
            string name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

            if (flag == 0xFF) return;

            Console.WriteLine(new string(' ', indent) + name);

            if (p_FirstComp != -1)
            {
                int currentSpecOffset = p_FirstComp;

                while (currentSpecOffset != -1 && currentSpecOffset < prsStream.Length)
                {
                    prsStream.Seek(currentSpecOffset, SeekOrigin.Begin);

                    byte specFlag = prsReader.ReadByte();
                    int p_ChildComponent = prsReader.ReadInt32();
                    ushort multiplicity = prsReader.ReadUInt16();
                    int specNext = prsReader.ReadInt32();

                    if (specFlag != 0xFF)
                    {
                        for (int i = 0; i < multiplicity; i++)
                        {
                            Console.Write(new string(' ', indent + 2) + "|");

                            if (i == multiplicity - 1 && specNext == -1)
                                Console.Write("__");
                            else
                                Console.Write("--");

                            PrintComponentTree(p_ChildComponent, "", indent + 4,
                                prdStream, prsStream, prdReader, prsReader);
                        }
                    }

                    currentSpecOffset = specNext;
                }
            }
        }

        public void Print(string name)
        {
            if (!IsOpen) throw new Exception("Файл не открыт");

            if (name == "*")
            {
                PrintAll();
                return;
            }

            string prsFileName = Encoding.UTF8.GetString(Header.NameSpec).TrimEnd('\0');

            if (!File.Exists(prsFileName))
            {
                Console.WriteLine($"Файл спецификаций {prsFileName} не найден");
                return;
            }

            PRS prsFile = new PRS(prsFileName);
            prsFile.Open();
            prsFile.Print(name);
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
                    Console.WriteLine("=== PRD HEADER ===");
                    Console.WriteLine($"Signature: {Encoding.ASCII.GetString(Header.Signature)}");
                    Console.WriteLine($"RecordLen: {Header.RecordLen}");
                    Console.WriteLine($"p_FirstRecord: {Header.p_FirstRecord} (0x{Header.p_FirstRecord:X})");
                    Console.WriteLine($"p_FreeSpace: {Header.p_FreeSpace}");
                    Console.WriteLine($"NameSpec: {Encoding.UTF8.GetString(Header.NameSpec).TrimEnd('\0')}");
                    Console.WriteLine();

                    if (Header.p_FirstRecord == -1)
                    {
                        Console.WriteLine("Записей нет.");
                        return;
                    }

                    Console.WriteLine("=== PRD RECORDS ===");
                    Console.WriteLine($"{"Offset",-10} {"Flag",-6} {"p_FirstComp",-14} {"p_Next",-10} {"Name"}");
                    Console.WriteLine(new string('-', 60));

                    int offset = Header.p_FirstRecord;
                    int recordCount = 0;

                    while (offset != -1 && offset < fs.Length)
                    {
                        fs.Seek(offset, SeekOrigin.Begin);

                        byte flag = br.ReadByte();
                        int p_FirstComp = br.ReadInt32();
                        int p_Next = br.ReadInt32();
                        byte[] nameBytes = br.ReadBytes(Header.RecordLen);
                        string name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

                        string type = p_FirstComp == -1 ? "Деталь" : "Узел/Изделие";
                        string status = flag == 0xFF ? "DELETED" : "ACTIVE";
                        string flagStr = flag == 0xFF ? "FF" : "00";

                        Console.WriteLine($"{offset,-10:X} {flagStr,-6} {p_FirstComp,-14:X} {p_Next,-10:X} {name} ({type}) [{status}]");

                        offset = p_Next;
                        recordCount++;
                    }

                    Console.WriteLine(new string('-', 60));
                    Console.WriteLine($"Всего записей: {recordCount}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Ошибка чтения: " + ex.Message);
            }
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

                    if (read.IsDeleted)
                    {
                        fs.Seek(currentOffset, SeekOrigin.Begin);
                        bw.Write((byte)0x00);
                        restoredCount++;
                        Console.WriteLine($"Восстановлен компонент: {nameStr}");
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
                            bw.Write(0);
                            bw.Write(read.Name);

                            if (lastValidOffset != -1)
                            {
                                long currentPos = destFs.Position;
                                destFs.Seek(lastValidOffset + 5, SeekOrigin.Begin);
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

                    Console.WriteLine($"{"Имя компонента",-20} {"Тип",-15} {"Статус"}");
                    Console.WriteLine(new string('-', 45));

                    while (offset != -1 && offset < fs.Length)
                    {
                        fs.Seek(offset, SeekOrigin.Begin);

                        byte flag = br.ReadByte();
                        int p_FirstComp = br.ReadInt32();
                        int p_Next = br.ReadInt32();
                        byte[] nameBytes = br.ReadBytes(Header.RecordLen);
                        string name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

                        string type = p_FirstComp == -1 ? "Деталь" : "Узел/Изделие";
                        string status = flag == 0xFF ? "[УДАЛЕН]" : "[АКТИВЕН]";

                        Console.WriteLine($"{name,-20} {type,-15} {status}");

                        offset = p_Next;
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