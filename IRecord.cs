using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TMPLAB1
{
    public interface IRecord
    {
        public byte FlagDelete { get; set; }
        public int p_Next { get; set; }
    }
}
