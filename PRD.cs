using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TMPLAB1
{
    public class FileHeaderPRD
    {
        public byte[] Signature { get; set; } = new byte[2];
        public ushort RecordLen { get; set; }
        public int p_FirstRec { get; set; }
        public int p_FreeSpace { get; set; }
        public byte[] NameSpec { get; set; } = new byte[16];
        public bool IsOpen { get; set; }

        public FileHeaderPRD()
        {
            // Устанавливаем сигнатуру
            Signature[0] = (byte)'P';
            Signature[1] = (byte)'S';
            IsOpen = false;
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