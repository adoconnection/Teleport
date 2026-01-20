using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Teleport.Shared
{
    public class Entry
    {
        public string Path { get;set; }
        public bool IsDirectory { get;set;}
        public long Size { get;set;}
    }
}
