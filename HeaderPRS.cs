using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TMPLAB1
{
    public class HeaderPRS : IFileHeader
    {
        public int p_FirstRecord { get; set; }
        public int p_FreeSpace { get; set; }
    }
}
