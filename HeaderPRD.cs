using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TMPLAB1
{
    public class HeaderPRD : IFileHeader
    {
        private ushort recordLen;
        
        public byte[] Signature { get; set; } = new byte[2];
        public int p_FirstRecord { get; set; }
        public int p_FreeSpace { get; set; }
        public byte[] NameSpec { get; set; }
        public ushort RecordLen
        {
            get => recordLen;
            set
            {
                if (value < 8)
                    throw new Exception("Длина записи слишком маленькая");

                if (value > 1024)
                    throw new Exception("Длина записи слишком большая");

                recordLen = value;
            }
        }
        public HeaderPRD()
        {
            Signature[0] = (byte)'P';
            Signature[1] = (byte)'S';
        }
    }
}
