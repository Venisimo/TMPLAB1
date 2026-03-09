using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TMPLAB1
{
    public interface IFile
    {
        public void Create();
        public void Open();
        public void Input(string argument);
        public void Delete(string name);
        public void Print(string name);
        public void Restore(string name);
        public void Truncate();
        public void PrintDev();

        public bool IsOpen { get; set; }
        public string CurrentFileName { get; set; }
        public IFileHeader FileHeader { get; set; }
        public IRecord Record { get; set; }
    }
}
