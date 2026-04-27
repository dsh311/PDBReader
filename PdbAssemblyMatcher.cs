using Dia2Lib;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace PDBReader
{
    public class PdbAssemblyMatcher
    {

        // Asumption the .pdb ends with .pdb
        public static List<PdbInfo> ScanPdbFolder(string folder)
        {
            var result = new List<PdbInfo>();
            foreach (var pdbPath in Directory.EnumerateFiles(folder, "*.pdb"))
            {
                try
                {
                    result.Add(ReadPdbInfo(pdbPath));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to read {pdbPath}: {ex.Message}");
                }
            }
            return result;
        }

        public static List<AssemblyInfo> ScanAssemblyFolder(string folder)
        {
            var result = new List<AssemblyInfo>();

            // Recursively enumerate all files in folder and subfolders
            foreach (var filePath in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
            {
                try
                {
                    // Try opening as PE file
                    using var stream = File.OpenRead(filePath);
                    using var peReader = new PEReader(stream);


                    // Check PEHeader first
                    if (peReader.PEHeaders?.PEHeader == null)
                    {
                        // Not a valid PE, skip it
                        continue;
                    }

                    // If no exceptions, treat it as an assembly/PE
                    var asmInfo = ReadAssemblyInfo(filePath);
                    result.Add(asmInfo);
                }
                catch (BadImageFormatException)
                {
                    // Not a valid PE file — ignore
                    continue;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to read {filePath}: {ex.Message}");
                }
            }

            return result;
        }


        static PdbInfo ReadWindowsPdb(string pdbPath)
        {
            // From DIA’s point of view, both a native (C/C++) PDB and
            // a .NET Framework PDB (non-portable) are the same file format.

            // Create DIA DataSource
            var diaSource = new DiaSource();
            diaSource.loadDataFromPdb(pdbPath);

            // Open session
            diaSource.openSession(out IDiaSession session);

            // Access the PDB GUID and Age
            IDiaSymbol globalScope = session.globalScope;

            var pdbGuid = globalScope.guid; // returns System.Guid
            var pdbAge = globalScope.age;   // returns int

            return new PdbInfo
            {
                Path = pdbPath,
                IsPortable = false,
                WindowsGuid = pdbGuid,
                WindowsAge = (int)pdbAge
            };
        }

        public static PdbInfo ReadPdbInfo(string pdbPath)
        {
            if (!File.Exists(pdbPath))
            {
                throw new FileNotFoundException(pdbPath);
            }

            // Handle portable .pdbs
            // .NET Core / .NET 5 +
            // .NET Framework if built with / pdb:portable(VS 2017 +)
            // Portable PDBs from Roslyn-based builds
            if (IsPortablePdb(pdbPath))
            {
                using var stream = File.OpenRead(pdbPath);
                using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
                var reader = provider.GetMetadataReader();

                // The reader.DebugMetadataHeader.Id is a [ 16-byte GUID ][ 4-byte stamp ] defined by ECMA-335

                var idBytes = reader.DebugMetadataHeader.Id.ToArray();

                var guidBytes = new byte[16];
                Array.Copy(idBytes, 0, guidBytes, 0, 16);

                var stamp = BitConverter.ToInt32(idBytes, 16);

                return new PdbInfo
                {
                    Path = pdbPath,
                    IsPortable = true,
                    PortableGuid = new Guid(guidBytes),
                    PortableStamp = stamp
                };
            }
            else
            {
                // Classic/native PDB
                // Traditional .NET Framework PDBs are Windows/native PDBs
                return ReadWindowsPdb(pdbPath);
            }
        }

        public static AssemblyInfo ReadAssemblyInfo(string assemblyPath)
        {
            var arch = ArchitectureType.Unknown;

            try
            {
                using var stream = File.OpenRead(assemblyPath);
                using var peReader = new PEReader(stream);

                // Determine architecture
                switch (peReader.PEHeaders.CoffHeader.Machine)
                {
                    case Machine.I386: arch = ArchitectureType.x86; break;
                    case Machine.Amd64: arch = ArchitectureType.x64; break;
                    case Machine.Arm: arch = ArchitectureType.Arm; break;
                    case Machine.Arm64: arch = ArchitectureType.Arm64; break;
                    default: arch = ArchitectureType.Unknown; break;
                }

                // Attempt to read debug directory
                ImmutableArray<DebugDirectoryEntry> debugEntries;
                try
                {
                    debugEntries = peReader.ReadDebugDirectory();
                }
                catch
                {
                    // Failed to read debug info; return minimal AssemblyInfo
                    return new AssemblyInfo
                    {
                        Path = assemblyPath,
                        Architecture = arch
                    };
                }

                // Look for CodeView entry
                foreach (var entry in debugEntries)
                {
                    if (entry.Type == DebugDirectoryEntryType.CodeView && entry.DataSize > 0)
                    {
                        var cvData = peReader.ReadCodeViewDebugDirectoryData(entry);

                        return new AssemblyInfo
                        {
                            Path = assemblyPath,
                            Architecture = arch,

                            CodeViewGuid = cvData.Guid,
                            CodeViewAgeOrStamp = cvData.Age,
                        };
                    }
                }


                // No CodeView found — return minimal info
                return new AssemblyInfo
                {
                    Path = assemblyPath,
                    Architecture = arch
                };
            }
            catch (BadImageFormatException)
            {
                // Not a PE file — return null or minimal info if you prefer
                return new AssemblyInfo
                {
                    Path = assemblyPath,
                    Architecture = arch
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to read {assemblyPath}: {ex.Message}");
                return new AssemblyInfo
                {
                    Path = assemblyPath,
                    Architecture = arch
                };
            }
        }

        public static bool Match(PdbInfo pdb, AssemblyInfo asm)
        {
            if (pdb == null || asm == null)
            {
                return false;
            }

            if (pdb.IsPortable)
            {
                /*
                Console.WriteLine("PORTABLE PDB============================");
                Console.WriteLine("PDB is: " + Path.GetFileName(pdb.Path));
                Console.WriteLine("   Pdb has Guild: " + pdb.PortableGuid.HasValue);
                Console.WriteLine("   Pdb has stamp: " + pdb.PortableStamp.HasValue);
                Console.WriteLine("   Pdb Guid: " + pdb.PortableGuid.Value);
                Console.WriteLine("   Pdb Stamp: " + pdb.PortableStamp.Value);

                Console.WriteLine("Assembly is: " + Path.GetFileName(asm.Path));
                Console.WriteLine("   Assmebly has Guild: " + asm.CodeViewGuid.HasValue);
                Console.WriteLine("   Assmebly has ageOrstamp: " + asm.CodeViewAgeOrStamp.HasValue);
                Console.WriteLine("   Assmebly Guid: " + asm.CodeViewGuid.Value);
                Console.WriteLine("   Assmebly AgeOrStamp: " + asm.CodeViewAgeOrStamp.Value);
                Console.WriteLine("============================");
                */

                // For portable PDBs, match only GUID
                bool portablePdbMatched = pdb.PortableGuid.HasValue &&
                       asm.CodeViewGuid.HasValue &&
                       pdb.PortableGuid.Value == asm.CodeViewGuid.Value;


                return portablePdbMatched;
            }
            else
            {


                // Using StringBuilder to accumulate the text
                /*
                StringBuilder sb = new StringBuilder();

                sb.AppendLine("WINDOWS PDB============================");
                sb.AppendLine($"Check PDB: {Path.GetFileName(pdb.Path)} = Assem: {Path.GetFileName(asm.Path)}");
                sb.AppendLine($"PDB is: {Path.GetFileName(pdb.Path)}");
                sb.AppendLine($"   Pdb has Guid: {pdb.WindowsGuid.HasValue}");
                sb.AppendLine($"   Pdb has Age: {pdb.WindowsAge.HasValue}");

                if (pdb.WindowsGuid.HasValue)
                {
                    sb.AppendLine($"   Pdb Guid: {pdb.WindowsGuid.Value}");
                }

                if (pdb.WindowsAge.HasValue)
                {
                    sb.AppendLine($"   Pdb Stamp: {pdb.WindowsAge.Value}");
                }

                sb.AppendLine($"Assembly is: {Path.GetFileName(asm.Path)}");
                sb.AppendLine($"   Assembly has Guid: {asm.CodeViewGuid.HasValue}");
                sb.AppendLine($"   Assembly has ageOrstamp: {asm.CodeViewAgeOrStamp.HasValue}");

                if (asm.CodeViewGuid.HasValue)
                {
                    sb.AppendLine($"   Assembly Guid: {asm.CodeViewGuid.Value}");
                }

                if (asm.CodeViewAgeOrStamp.HasValue)
                {
                    sb.AppendLine($"   Assembly AgeOrStamp: {asm.CodeViewAgeOrStamp.Value}");
                }

                sb.AppendLine("============================");

                // Optional: Print to console so you can still see it
                //Console.Write(sb.ToString());

                // Save to file
                try
                {
                    string saveFilePath = @"C:\Users\David\source\repos\PDBReader\bin\x64\Release\net8.0\AllCompare.txt";
                    // AppendAllText adds to the end of the file. 
                    // It automatically creates the file if it doesn't exist yet.
                    File.AppendAllText(saveFilePath, sb.ToString());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to save log: {ex.Message}");
                }
                */

                // Classic Windows PDB: match GUID + Age
                bool windowsPdbMatched = pdb.WindowsGuid.HasValue &&
                       pdb.WindowsAge.HasValue &&
                       asm.CodeViewGuid.HasValue &&
                       asm.CodeViewAgeOrStamp.HasValue &&
                       pdb.WindowsGuid.Value == asm.CodeViewGuid.Value &&
                       pdb.WindowsAge.Value == asm.CodeViewAgeOrStamp.Value;


                return windowsPdbMatched;
            }
        }


        static bool IsPortablePdb(string pdbPath)
        {
            using var fs = File.OpenRead(pdbPath);
            Span<byte> buffer = stackalloc byte[4];

            int read = fs.Read(buffer);
            if (read < 4) return false;

            // Portable PDBs start with "BSJB" in ASCII
            return buffer[0] == (byte)'B' &&
                   buffer[1] == (byte)'S' &&
                   buffer[2] == (byte)'J' &&
                   buffer[3] == (byte)'B';
        }
    }
}
