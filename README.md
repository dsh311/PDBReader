# PDBReader

PDBReader is a utility tool that matches and relocates `.pdb` (Program Database) symbol files based on their corresponding compiled assemblies. It performs signature-based validation to ensure symbol correctness before copying, helping prevent incorrect or misleading debugging information.

---

## 🔍 What Problem Does This Solve?

Symbol files (`.pdb`) are essential for debugging, but incorrect or mismatched PDBs can lead to:
- Broken stack traces
- Incorrect source mapping
- “Ghost” debugging sessions
- Misleading crash diagnostics

PDBReader ensures only valid, matching symbol files are paired with their assemblies.

---

## 📦 Supported PDB Types

PDBReader supports two major types of PDB files:

### 1. Classic / Windows Native PDB (DIA / Native PDB)
- Used for native C++ binaries
- Used for .NET Framework assemblies (older versions)
- Identity is defined by GUID + Age

### 2. Portable PDB
- Introduced for .NET Core / .NET Standard
- Common in .NET Core / .NET 5+ applications
- Identity is defined by GUID + Stamp

---

## 🧠 Matching & Safety Rules

Because mismatched PDBs can produce incorrect debugging results, PDBReader enforces a strict validation hierarchy:

1. Copy if GUID + Age matches (Windows Native PDBs)  
2. Copy if GUID matches (Portable PDBs)  
3. If an assembly has no CodeView data but a PDB exists with the same name, copy it  

⚠️ Decision Rationale: It is safer to omit a symbol file than to provide a mismatched one, which can lead to misleading debugging behavior or “ghost” symbol resolution.

---

## ⚙️ Requirements

PDBReader requires the Microsoft DIA SDK to function properly.

Install the Visual C++ Redistributable (2015–2022) to ensure DIA support is available.

---

## 🧪 DIA SDK Test Mode

PDBReader includes a diagnostic flag to verify whether the DIA SDK is installed:

PDBReader.exe --test-dia

If installed, the program outputs that DIA is detected and exits with code 0.  
If not installed, it outputs that DIA is missing and exits with code 1.

---

## 🖥️ Command Line Usage

PDBReader supports two execution modes:

### DIA Test Mode
PDBReader.exe --test-dia  
Checks whether the Microsoft DIA SDK is installed.

### Normal Mode
PDBReader.exe "<directory of assemblies>" "<directory of pdbs>"

Example:  
PDBReader.exe "C:\Build\Release" "C:\Symbols"

Behavior:
- Scans assembly directory
- Scans PDB directory
- Performs signature-based matching
- Copies valid PDB files into correct locations

---

## 🧾 Program Flow

The application entry logic is:

- If --test-dia is passed:
  - Check DIA installation
  - Print result
  - Exit with code 0 if installed, 1 if not installed

- If DIA SDK is missing:
  - Print error message
  - Exit with code 1

- If invalid arguments are provided:
  - Print usage: PDBReader.exe "<dir path of assemblies>" "<dir path of pdbs>"
  - Exit with code 1

- Otherwise:
  - Execute PDB matching process

---

## 📌 Summary

PDBReader ensures safe and deterministic symbol file handling by:
- Supporting both legacy and modern PDB formats
- Validating symbol identity using strict matching rules
- Preventing incorrect debugging symbol assignment
- Providing a simple CLI workflow for automation

---

## 📄 License

This tool is provided for development and debugging utility purposes.
