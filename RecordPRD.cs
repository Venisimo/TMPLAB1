using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TMPLAB1
{
    public class RecordPRD : IRecord
    {
        public byte FlagDelete { get; set; }
        public int p_FirstComp { get; set; }
        public int p_Next { get; set; }
        public byte[] Name { get; set; }

        public bool IsDeleted => FlagDelete == 0xFF;
        public bool IsDetail => p_FirstComp == -1;
        public bool IsAssembly => p_FirstComp != -1;

        public RecordPRD()
        { 
        
        }

        public RecordPRD(byte flagDelete, int p_FirstComp, int p_Next, byte[] name)
        {
            FlagDelete = flagDelete;
            this.p_FirstComp = p_FirstComp;
            this.p_Next = p_Next;
            Name = name;
        }
    }
}
