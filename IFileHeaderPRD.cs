using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TMPLAB1
{
    public interface IFileHeaderPRD
    {
        public void Create(string fileName);
        public void Open(string fileName);
        public void Input(string argument);
        public void Delete(string name);
        public void Print(string name);
        public void Restore(string name);

        public void Truncate();
    }
}
