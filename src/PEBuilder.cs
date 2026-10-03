using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RawX
{
    public class PEBuilder
    {
        public static readonly List<string> DefaultImports = new List<string>
        {
            "ExitProcess", "GetStdHandle", "WriteFile", "WriteConsoleA", "ReadFile",
            "CreateFileA", "CloseHandle", "GetCommandLineA", "Sleep", "GetTickCount64",
            "VirtualAlloc", "VirtualFree", "HeapAlloc", "HeapFree", "GetProcessHeap"
        };

        public static int AlignUp(int val, int align)
        {
            return (val + align - 1) & ~(align - 1);
        }

        public static void Build(EncodedProgram prog, string outputPath)
        {
            const long imageBase = 0x140000000;
            const int fileAlign = 512;
            const int sectionAlign = 4096;
            const int textRva = 4096;

            int rawTextSize = AlignUp(Math.Max(1, prog.TextBytes.Count), fileAlign);
            int virtTextSize = AlignUp(rawTextSize, sectionAlign);

            int idataRva = textRva + virtTextSize;

            // Collect all required imports
            List<string> imports = new List<string>(DefaultImports);
            foreach (var reloc in prog.Relocations)
            {
                if (reloc.Type == "IMPORT_CALL" && !imports.Contains(reloc.TargetSymbol))
                {
                    imports.Add(reloc.TargetSymbol);
                }
            }

            int importCount = imports.Count;

            // Construct .idata
            // IDT: 1 descriptor (20 bytes) + null descriptor (20 bytes) = 40 bytes
            int idtSize = 40;
            int iltOffset = idtSize;
            int iltSize = (importCount + 1) * 8;
            int iatOffset = iltOffset + iltSize;
            int iatSize = iltSize;
            int dllNameOffset = iatOffset + iatSize;
            string dllName = "KERNEL32.DLL\0";
            byte[] dllNameBytes = Encoding.ASCII.GetBytes(dllName);
            int dllNameSize = AlignUp(dllNameBytes.Length, 8);
            int hintsOffset = dllNameOffset + dllNameSize;

            List<byte> idataPayload = new List<byte>();
            List<int> hintRvas = new List<int>();

            // Build Hint/Name Table
            List<byte> hintBytes = new List<byte>();
            foreach (string fn in imports)
            {
                hintRvas.Add(idataRva + hintsOffset + hintBytes.Count);
                hintBytes.AddRange(new byte[2]); // Hint 0
                hintBytes.AddRange(Encoding.ASCII.GetBytes(fn + "\0"));
                if ((hintBytes.Count % 2) != 0) hintBytes.Add(0); // align word
            }

            // IDT (20 bytes)
            idataPayload.AddRange(BitConverter.GetBytes(idataRva + iltOffset));     // OriginalFirstThunk (ILT)
            idataPayload.AddRange(new byte[8]);                                   // TimeDateStamp + ForwarderChain
            idataPayload.AddRange(BitConverter.GetBytes(idataRva + dllNameOffset)); // Name RVA
            idataPayload.AddRange(BitConverter.GetBytes(idataRva + iatOffset));     // FirstThunk (IAT)
            idataPayload.AddRange(new byte[20]);                                  // Null IDT terminator

            // ILT (Import Lookup Table)
            Dictionary<string, int> iatSymbolRvas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < importCount; i++)
            {
                idataPayload.AddRange(BitConverter.GetBytes((long)hintRvas[i]));
            }
            idataPayload.AddRange(new byte[8]); // null terminator

            // IAT (Import Address Table)
            for (int i = 0; i < importCount; i++)
            {
                int rva = idataRva + iatOffset + i * 8;
                iatSymbolRvas[imports[i]] = rva;
                idataPayload.AddRange(BitConverter.GetBytes((long)hintRvas[i]));
            }
            idataPayload.AddRange(new byte[8]); // null terminator

            // DLL Name
            idataPayload.AddRange(dllNameBytes);
            while (idataPayload.Count < hintsOffset) idataPayload.Add(0);

            // Hint/Names
            idataPayload.AddRange(hintBytes);

            int rawIdataSize = AlignUp(idataPayload.Count, fileAlign);
            int virtIdataSize = AlignUp(rawIdataSize, sectionAlign);

            int dataRva = idataRva + virtIdataSize;
            int totalDataBytes = prog.DataBytes.Count + prog.BssSize;
            bool hasData = totalDataBytes > 0;
            int rawDataSize = hasData ? AlignUp(prog.DataBytes.Count, fileAlign) : 0;
            int virtDataSize = hasData ? AlignUp(Math.Max(totalDataBytes, rawDataSize), sectionAlign) : 0;

            // Resolve Relocations
            foreach (var reloc in prog.Relocations)
            {
                int nextInstOffset = reloc.Offset + 4;
                int currentRva = textRva + nextInstOffset;

                if (reloc.Type == "IMPORT_CALL")
                {
                    if (iatSymbolRvas.ContainsKey(reloc.TargetSymbol))
                    {
                        int targetRva = iatSymbolRvas[reloc.TargetSymbol];
                        int delta = targetRva - currentRva;
                        byte[] deltaBytes = BitConverter.GetBytes(delta);
                        for (int b = 0; b < 4; b++)
                        {
                            prog.TextBytes[reloc.Offset + b] = deltaBytes[b];
                        }
                    }
                    else
                    {
                        throw new Exception("Unknown Windows Win64 API Import: " + reloc.TargetSymbol);
                    }
                }
                else if (reloc.Type == "REL32")
                {
                    if (prog.Symbols.ContainsKey(reloc.TargetSymbol))
                    {
                        SymbolLocation sym = prog.Symbols[reloc.TargetSymbol];
                        int symBase = (sym.Section == ".text") ? textRva :
                                      (sym.Section == ".bss") ? (dataRva + prog.DataBytes.Count) : dataRva;
                        int targetRva = symBase + sym.Offset;
                        int delta = targetRva - currentRva;
                        byte[] deltaBytes = BitConverter.GetBytes(delta);
                        for (int b = 0; b < 4; b++)
                        {
                            prog.TextBytes[reloc.Offset + b] = deltaBytes[b];
                        }
                    }
                }
                else if (reloc.Type == "RIP_REL32")
                {
                    if (prog.Symbols.ContainsKey(reloc.TargetSymbol))
                    {
                        SymbolLocation sym = prog.Symbols[reloc.TargetSymbol];
                        int symBase = (sym.Section == ".text") ? textRva :
                                      (sym.Section == ".bss") ? (dataRva + prog.DataBytes.Count) : dataRva;
                        int targetRva = symBase + sym.Offset;
                        int delta = (targetRva - currentRva) + reloc.Addend;
                        byte[] deltaBytes = BitConverter.GetBytes(delta);
                        for (int b = 0; b < 4; b++)
                        {
                            prog.TextBytes[reloc.Offset + b] = deltaBytes[b];
                        }
                    }
                }
            }

            // Headers size: 0x400 = 1024 bytes
            const int headersRaw = 1024;
            int textRawOffset = headersRaw;
            int idataRawOffset = textRawOffset + rawTextSize;
            int dataRawOffset = idataRawOffset + rawIdataSize;

            int totalImageSize = (hasData ? (dataRva + virtDataSize) : (idataRva + virtIdataSize));
            int totalFileSize = hasData ? (dataRawOffset + rawDataSize) : (idataRawOffset + rawIdataSize);

            byte[] pe = new byte[totalFileSize];

            // DOS Header (64 bytes)
            pe[0] = (byte)'M';
            pe[1] = (byte)'Z';
            Buffer.BlockCopy(BitConverter.GetBytes(128), 0, pe, 60, 4); // e_lfanew = 128 (0x80)

            // DOS Stub
            byte[] stub = Encoding.ASCII.GetBytes("This program was built by RawX.\r\n$");
            Buffer.BlockCopy(stub, 0, pe, 64, stub.Length);

            // PE Signature ("PE\0\0" at offset 128)
            pe[128] = (byte)'P';
            pe[129] = (byte)'E';

            // COFF Header (20 bytes at offset 132)
            short numSections = (short)(hasData ? 3 : 2);
            Buffer.BlockCopy(BitConverter.GetBytes(unchecked((short)0x8664)), 0, pe, 132, 2); // AMD64
            Buffer.BlockCopy(BitConverter.GetBytes(numSections), 0, pe, 134, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((short)240), 0, pe, 148, 2);    // SizeOfOptionalHeader
            Buffer.BlockCopy(BitConverter.GetBytes((short)0x0022), 0, pe, 150, 2); // EXECUTABLE_IMAGE | LARGE_ADDRESS_AWARE

            // Optional Header PE32+ (240 bytes at offset 152)
            Buffer.BlockCopy(BitConverter.GetBytes((short)0x020B), 0, pe, 152, 2); // PE32+ magic
            pe[154] = 1; // MajorLinkerVersion
            Buffer.BlockCopy(BitConverter.GetBytes(rawTextSize), 0, pe, 156, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(rawIdataSize + (hasData ? rawDataSize : 0)), 0, pe, 160, 4);

            // Entry Point
            int entryOffset = 0;
            if (prog.Symbols.ContainsKey(prog.EntrySymbol))
            {
                entryOffset = prog.Symbols[prog.EntrySymbol].Offset;
            }
            Buffer.BlockCopy(BitConverter.GetBytes(textRva + entryOffset), 0, pe, 168, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(textRva), 0, pe, 172, 4); // BaseOfCode

            Buffer.BlockCopy(BitConverter.GetBytes(imageBase), 0, pe, 176, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(sectionAlign), 0, pe, 184, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(fileAlign), 0, pe, 188, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((short)6), 0, pe, 192, 2); // OS Major
            Buffer.BlockCopy(BitConverter.GetBytes((short)6), 0, pe, 200, 2); // Subsystem Major
            Buffer.BlockCopy(BitConverter.GetBytes(totalImageSize), 0, pe, 208, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(headersRaw), 0, pe, 212, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((short)3), 0, pe, 220, 2); // Subsystem = Windows CUI (Console)
            Buffer.BlockCopy(BitConverter.GetBytes(unchecked((short)0x8160)), 0, pe, 222, 2); // DllCharacteristics
            Buffer.BlockCopy(BitConverter.GetBytes((long)0x100000), 0, pe, 224, 8); // Stack Reserve 1MB
            Buffer.BlockCopy(BitConverter.GetBytes((long)0x1000), 0, pe, 232, 8);   // Stack Commit 4KB
            Buffer.BlockCopy(BitConverter.GetBytes((long)0x100000), 0, pe, 240, 8); // Heap Reserve 1MB
            Buffer.BlockCopy(BitConverter.GetBytes((long)0x1000), 0, pe, 248, 8);   // Heap Commit 4KB
            Buffer.BlockCopy(BitConverter.GetBytes(16), 0, pe, 260, 4);             // NumberOfRvaAndSizes

            // Data Directories:
            // Dir 1: Import Table (RVA = idataRva, Size = 40)
            Buffer.BlockCopy(BitConverter.GetBytes(idataRva), 0, pe, 264 + 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(40), 0, pe, 264 + 12, 4);
            // Dir 12: IAT (RVA = idataRva + iatOffset, Size = iatSize)
            Buffer.BlockCopy(BitConverter.GetBytes(idataRva + iatOffset), 0, pe, 264 + 96, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(iatSize), 0, pe, 264 + 100, 4);

            // Section 1: .text (offset 392)
            byte[] textName = Encoding.ASCII.GetBytes(".text\0\0\0");
            Buffer.BlockCopy(textName, 0, pe, 392, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(prog.TextBytes.Count), 0, pe, 400, 4); // VirtualSize
            Buffer.BlockCopy(BitConverter.GetBytes(textRva), 0, pe, 404, 4);              // VirtualAddress
            Buffer.BlockCopy(BitConverter.GetBytes(rawTextSize), 0, pe, 408, 4);          // SizeOfRawData
            Buffer.BlockCopy(BitConverter.GetBytes(textRawOffset), 0, pe, 412, 4);        // PointerToRawData
            Buffer.BlockCopy(BitConverter.GetBytes((int)0x60000020), 0, pe, 428, 4);      // CODE | EXECUTE | READ

            // Section 2: .idata (offset 432)
            byte[] idataName = Encoding.ASCII.GetBytes(".idata\0\0");
            Buffer.BlockCopy(idataName, 0, pe, 432, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(idataPayload.Count), 0, pe, 440, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(idataRva), 0, pe, 444, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(rawIdataSize), 0, pe, 448, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(idataRawOffset), 0, pe, 452, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(unchecked((int)0xC0000040)), 0, pe, 468, 4); // INITIALIZED_DATA | READ | WRITE

            // Section 3: .data (offset 472)
            if (hasData)
            {
                byte[] dataName = Encoding.ASCII.GetBytes(".data\0\0\0");
                Buffer.BlockCopy(dataName, 0, pe, 472, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(totalDataBytes), 0, pe, 480, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(dataRva), 0, pe, 484, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(rawDataSize), 0, pe, 488, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(dataRawOffset), 0, pe, 492, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(unchecked((int)0xC0000040)), 0, pe, 508, 4);
            }

            // Copy payload bytes into PE buffer
            prog.TextBytes.CopyTo(pe, textRawOffset);
            idataPayload.CopyTo(pe, idataRawOffset);
            if (hasData && prog.DataBytes.Count > 0)
            {
                prog.DataBytes.CopyTo(pe, dataRawOffset);
            }

            // Write to disk
            string outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }
            File.WriteAllBytes(outputPath, pe);
        }
    }
}
