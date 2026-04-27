//using Microsoft.DiaSymReader.PortablePdb;
using Dia2Lib; // COM reference to Microsoft DIA SDK
using Microsoft.DiaSymReader;
using System;
using System.Data;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace PDBReader
{
    internal class Program
    {
        static bool IsDiaInstalled()
        {
            try
            {
                var dia = new DiaSource(); // constructor will throw if DIA isn't installed/registered
                return true;
            }
            catch (COMException ex)
            {
                Console.WriteLine($"DIA not available: 0x{ex.ErrorCode:X8}");
                return false;
            }
        }

        static string GetRelativePath(string baseDir, string fullPath)
        {
            if (!baseDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                baseDir += Path.DirectorySeparatorChar;
            }

            var baseUri = new Uri(baseDir);
            var fileUri = new Uri(fullPath);

            return Uri.UnescapeDataString(
                baseUri.MakeRelativeUri(fileUri)
                       .ToString()
                       .Replace('/', Path.DirectorySeparatorChar)
            );
        }

        static int Main(string[] args)
        {
            // Handle --test-dia early
            if (args.Length == 1 && args[0].Equals("--test-dia", StringComparison.OrdinalIgnoreCase))
            {
                bool diaInstalled = IsDiaInstalled();
                Console.WriteLine(diaInstalled
                    ? "   PDBReader detects Microsoft DIA SDK is installed."
                    : "   PDBReader detects Microsoft DIA SDK is NOT installed.");
                return diaInstalled ? 0 : 1; // exit code
            }

            if (!IsDiaInstalled())
            {
                Console.Error.WriteLine(
                    "Microsoft DIA SDK not found. Please install the Visual C++ Redistributable (2015–2022)."
                );
                return 1;
            }
            
            if (args.Length != 2)
            {
                Console.WriteLine("Usage: PDBReader.exe \"<dir path of assemblies>\" \"<dir path of pdbs>\"");
                return 1;
            }

            string assemblyDirPath = args[0];
            string pdbDirPath = args[1];
            

            var assemblies = PdbAssemblyMatcher.ScanAssemblyFolder(assemblyDirPath);

            var pdbs = PdbAssemblyMatcher.ScanPdbFolder(pdbDirPath);
            

            int matchedPDBCount = 0;

            // First, match assemblies to PDBs (optional)
            foreach (var asm in assemblies)
            {
                var matchingPdb = pdbs.FirstOrDefault(p => PdbAssemblyMatcher.Match(p, asm));
                if (matchingPdb != null)
                {
                    matchedPDBCount++;

                    var asmDir = Path.GetDirectoryName(asm.Path)!;
                    var pdbFileName = Path.GetFileName(matchingPdb.Path);
                    var destinationPath = Path.Combine(asmDir, pdbFileName);

                    // Don't try and copy over itself.
                    // If user points to the same directory for verification,
                    // this could be attempted, so prevent it
                    if (!Path.GetFullPath(matchingPdb.Path).Equals(Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(matchingPdb.Path, destinationPath, overwrite: true);
                    }

                    // Print info to console
                    string relativeAsmPath = GetRelativePath(assemblyDirPath, asm.Path);

                    string pdbType = matchingPdb.IsPortable ? "Portable" : "Windows";

                    Console.WriteLine(
                        $"Copied matching {pdbType} PDB {pdbFileName} for Assembly {relativeAsmPath}"
                    );
                }
            }

            Console.WriteLine($"\nSub Total of Perfect Match Count = {matchedPDBCount} of {pdbs.Count} PDB file(s) copied because PERFECT matching assembly found.");

            Console.WriteLine("@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@\n");



            // Materialize once to avoid repeated enumeration
            var unmatchedPDBs = pdbs
                .Where(pdb => !assemblies.Any(asm => PdbAssemblyMatcher.Match(pdb, asm)))
                .ToList();

            int matchedBestEffortPDBCount = 0;

            // TAKE THE unmatched and try and match based on name if the assembly exists and is missing CodeView
            // Iterate backwards so we can safely remove items
            for (int i = unmatchedPDBs.Count - 1; i >= 0; i--)
            {
                var pdb = unmatchedPDBs[i];
                var pdbFileName = Path.GetFileNameWithoutExtension(pdb.Path);

                // Find all assemblies with the same name
                var matchingAssemblies = assemblies
                    .Where(asm => Path.GetFileNameWithoutExtension(asm.Path)
                                     .Equals(pdbFileName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matchingAssemblies.Count > 0)
                {
                    bool copied = false;

                    foreach (var asm in matchingAssemblies)
                    {
                        // Only do best-effort if assembly is missing GUID
                        if (!asm.CodeViewGuid.HasValue)
                        {
                            string relativeAsmPath = GetRelativePath(assemblyDirPath, asm.Path);
                            string pdbType = pdb.IsPortable ? "Portable" : "Windows";
                            Console.WriteLine(
                                $"Copied best-effort matching {pdbType} PDB {pdbFileName} for Assembly {relativeAsmPath}"
                            );

                            //Console.WriteLine($"Best-effort match: PDB {pdb.Path} -> Assembly {asm.Path}");

                            // Copy file
                            var asmDir = Path.GetDirectoryName(asm.Path)!;
                            var pdbFileNameWithExt = Path.GetFileName(pdb.Path);
                            var destinationPath = Path.Combine(asmDir, pdbFileNameWithExt);

                            // Don't try and copy over itself.
                            // If user points to the same directory for verification,
                            // this could be attempted, so prevent it
                            if (!Path.GetFullPath(pdb.Path).Equals(Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
                            {
                                File.Copy(pdb.Path, destinationPath, overwrite: true);
                            }


                            copied = true;
                            matchedBestEffortPDBCount++;
                        }
                        else
                        {
                            //Console.WriteLine($"Skipped PDB {pdb.Path} -> Assembly {asm.Path} (assembly already has CodeView)");
                        }
                    }

                    // Remove from unmatchedPDBs only if we copied to at least one assembly
                    if (copied)
                    {
                        unmatchedPDBs.RemoveAt(i);
                    }
                }
                else
                {
                    //Console.WriteLine($"No assembly with matching name found for PDB {pdb.Path}");
                }
            }

            if (matchedBestEffortPDBCount > 0)
            {
                Console.WriteLine($"\nSub Total of Best Effort Match Count = {matchedBestEffortPDBCount} of {pdbs.Count} PDB file(s) copied because BEST-EFFORT matching assembly found.");
                Console.WriteLine("@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@\n");
            }


            // Now, list all PDBs that did NOT match any assembly
            // Determine destination directory for unmatched PDBs
            var unmatchedPdbDir = assemblyDirPath + "_UnmatchedPDBs";
            Directory.CreateDirectory(unmatchedPdbDir);

            foreach (var pdb in unmatchedPDBs)
            {
                var pdbFileName = Path.GetFileName(pdb.Path);
                var destinationPath = Path.Combine(unmatchedPdbDir, pdbFileName);

                // Don't try and copy over itself.
                // If user points to the same directory for verification,
                // this could be attempted, so prevent it
                if (!Path.GetFullPath(pdb.Path).Equals(Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(pdb.Path, destinationPath, overwrite: true);
                }

                string pdbType = pdb.IsPortable ? "Portable" : "Windows";

                Console.WriteLine(pdbType + " pdb " + pdbFileName);
            }

            if (unmatchedPDBs.Count > 0)
            {
                Console.WriteLine($"\nSub Total of Missing Match Count = {unmatchedPDBs.Count} of {pdbs.Count} PDB file(s) NOT copied because NO machting assembly found.");
                Console.WriteLine("@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@\n");
            }

            int totalMatchCount = matchedPDBCount + matchedBestEffortPDBCount;

            Console.WriteLine($"\nTotal Match Count = {totalMatchCount} of {pdbs.Count} PDB file(s) copied because machting assembly found.");
            if (totalMatchCount == pdbs.Count)
            {
                Console.WriteLine($"All pdb files matched to assemblies!!!");
            }

            Console.WriteLine("\nPDB analysis completed.");
            Console.WriteLine("");

            return 0;

        }

        static PdbType DetectPdbType(string pdbPath)
        {
            using (var fs = new FileStream(pdbPath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                var header = br.ReadBytes(4);
                string magic = System.Text.Encoding.ASCII.GetString(header);
                if (magic == "BSJB")
                    return PdbType.Portable;
                else
                    return PdbType.Windows; // could be native or .NET
            }
        }

        enum PdbType
        {
            Portable,
            Windows
        }


        static bool IsManagedAssembly(string assemblyPath)
        {
            try
            {
                var asmName = System.Reflection.AssemblyName.GetAssemblyName(assemblyPath);
                return true; // it's a .NET assembly
            }
            catch (BadImageFormatException)
            {
                return false; // native binary
            }
        }

        static void ReadNativePdb(string pdbPath)
        {
            // Create DIA DataSource
            var diaSource = new DiaSource();
            diaSource.loadDataFromPdb(pdbPath);

            // Open session
            diaSource.openSession(out IDiaSession session);

            // Get global scope
            IDiaSymbol globalScope = session.globalScope;

            // Prepare the out variable
            IDiaEnumSymbols functions;
            globalScope.findChildren(
                SymTagEnum.SymTagFunction,
                null,  // no name filter
                0,     // case insensitive
                out functions);

            // Enumerate all functions
            foreach (IDiaSymbol func in functions)
            {
                string name = func.name;
                ulong rva = func.relativeVirtualAddress;
                ulong length = func.length;

                Console.WriteLine($"{name}  RVA=0x{rva:X}  Length={length}");
            }

            Console.WriteLine("Finished reading native PDB.");
        }


        static bool IsPortablePdb(string pdbPath)
        {
            // Portable PDBs start with "BSJB" in ASCII at the start
            using var fs = File.OpenRead(pdbPath);
            Span<byte> buffer = stackalloc byte[4];
            fs.Read(buffer);
            return buffer.SequenceEqual(new byte[] { (byte)'B', (byte)'S', (byte)'J', (byte)'B' });
        }


        // Portable PDBs (System.Reflection.Metadata) are only for managed .NET
        static void ReadPortablePdb(string pdbPath)
        {
            using var stream = File.OpenRead(pdbPath);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            var reader = provider.GetMetadataReader();

            foreach (var methodHandle in reader.MethodDefinitions)
            {
                var method = reader.GetMethodDefinition(methodHandle);
                var name = reader.GetString(method.Name);
                Console.WriteLine(name);
            }
        }

        // Only for managed .NET PDBs (classic Windows PDB format for .NET assemblies). It cannot read native C++ PDBs.
        // Note: Microsoft.DiaSymReader.Native cannot handle native PDBs. It only helps read managed .NET PDBs.
        static void ReadWindowsPdb(string assemblyPath, string pdbPath)
        {
            // Load the unmanaged DIA symbol reader
            // Ensure you have Microsoft.DiaSymReader.Native installed for this
            var symReader = SymUnmanagedReaderFactory.CreateReaderForFile(assemblyPath, pdbPath);

            int methodCount = symReader.GetMethodCount();
            for (int i = 0; i < methodCount; i++)
            {
                string name = symReader.GetMethodName(i);
                Console.WriteLine(name);
            }
        }

    }

    // Minimal helper wrapper for ISymUnmanagedReader (classic PDB)
    // You will need Microsoft.DiaSymReader.Native for this
    public static class SymUnmanagedReaderFactory
    {
        public static WindowsPdbReader CreateReaderForFile(string assemblyPath, string pdbPath)
        {
            // Initialize COM-based unmanaged reader
            return new WindowsPdbReader(pdbPath);
        }
    }

    // Simplified WindowsPdbReader
    public class WindowsPdbReader
    {
        private readonly string _pdbPath;
        public WindowsPdbReader(string pdbPath) => _pdbPath = pdbPath;

        public int GetMethodCount()
        {
            return 0;
        }

        public string GetMethodName(int index)
        {
            return $"Method_{index}";
        }
    }



}
