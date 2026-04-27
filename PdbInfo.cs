using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBReader
{
    public class PdbInfo
    {
        public string Path { get; set; }
        public bool IsPortable { get; set; }


        // For portable PDBs, the identity is:
        // PortablePdbId = GUID(16 bytes) + Stamp(4 bytes)

        // Portable PDB
        public Guid? PortableGuid { get; set; }
        public int? PortableStamp { get; set; }

        // For classic Windows PDBs(native/.NET), the identity is:
        // WindowsPdbId = GUID + Age
        public Guid? WindowsGuid { get; set; }
        public int? WindowsAge { get; set; }

        
        
        
        public override string ToString()
        {
            if (IsPortable)
                return $"Portable PDB: {Path}, GUID={PortableGuid}";
            else
                return $"Windows PDB: {Path}, GUID={WindowsGuid}, Age={WindowsAge}";
        }
    }
}
