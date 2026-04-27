using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBReader
{
    public enum ArchitectureType
    {
        Unknown,
        x86,
        x64,
        Arm,
        Arm64
    }

    public class AssemblyInfo
    {
        public string Path { get; set; }

        // CodeView debug directory
        public Guid? CodeViewGuid { get; set; }
        public int? CodeViewAgeOrStamp { get; set; }

        public ArchitectureType Architecture { get; set; }

        public override string ToString()
        {
            return $"Assembly: {Path}, GUID={CodeViewGuid}, Value={CodeViewAgeOrStamp}, Arch={Architecture}";
        }
    }

}
