using System;
using System.Collections.Generic;
using System.IO;

namespace RawX
{
    public class ELFBuilder
    {
        public static int AlignUp(int val, int align)
        {
            return (val + align - 1) & ~(align - 1);
        }

        public static void Build(EncodedProgram prog, string outputPath)
        {
            const long baseVAddr = 0x400000;
            const int pageSize = 0x1000; // 4096

            int headerSize = 64; // ELF64 Header
            bool hasData = (prog.DataBytes.Count + prog.BssSize) > 0;
            int numPhdrs = hasData ? 2 : 1;
            int phdrsSize = numPhdrs * 56; // 56 bytes per Elf64_Phdr

            int totalHeaderSize = headerSize + phdrsSize;
            int textFileOffset = pageSize; // 0x1000
            long textVAddr = baseVAddr + textFileOffset; // 0x401000

            int rawTextSize = Math.Max(1, prog.TextBytes.Count);
            int alignedTextSize = AlignUp(rawTextSize, pageSize);

            int dataFileOffset = textFileOffset + alignedTextSize;
            long dataVAddr = textVAddr + alignedTextSize;

            int rawDataSize = prog.DataBytes.Count;
            int totalDataMemSize = rawDataSize + prog.BssSize;

            // Resolve Relocations
            foreach (var reloc in prog.Relocations)
            {
                int nextInstOffset = reloc.Offset + 4;
                long currentVAddr = textVAddr + nextInstOffset;

                if (reloc.Type == "REL32")
                {
                    if (prog.Symbols.ContainsKey(reloc.TargetSymbol))
                    {
                        SymbolLocation sym = prog.Symbols[reloc.TargetSymbol];
                        long symBase = (sym.Section == ".text") ? textVAddr :
                                       (sym.Section == ".bss") ? (dataVAddr + prog.DataBytes.Count) : dataVAddr;
                        long targetVAddr = symBase + sym.Offset;
                        long delta = targetVAddr - currentVAddr;
                        byte[] deltaBytes = BitConverter.GetBytes((int)delta);
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
                        long symBase = (sym.Section == ".text") ? textVAddr :
                                       (sym.Section == ".bss") ? (dataVAddr + prog.DataBytes.Count) : dataVAddr;
                        long targetVAddr = symBase + sym.Offset;
                        long delta = (targetVAddr - currentVAddr) + reloc.Addend;
                        byte[] deltaBytes = BitConverter.GetBytes((int)delta);
                        for (int b = 0; b < 4; b++)
                        {
                            prog.TextBytes[reloc.Offset + b] = deltaBytes[b];
                        }
                    }
                }
            }

            // Entry point address
            long entryAddr = textVAddr;
            if (prog.Symbols.ContainsKey(prog.EntrySymbol))
            {
                entryAddr = textVAddr + prog.Symbols[prog.EntrySymbol].Offset;
            }
            else if (prog.Symbols.ContainsKey("_start"))
            {
                entryAddr = textVAddr + prog.Symbols["_start"].Offset;
            }
            else if (prog.Symbols.ContainsKey("main"))
            {
                entryAddr = textVAddr + prog.Symbols["main"].Offset;
            }

            int totalFileSize = hasData ? (dataFileOffset + rawDataSize) : (textFileOffset + rawTextSize);
            byte[] elf = new byte[totalFileSize];

            // 1. ELF Header (64 bytes)
            // e_ident
            elf[0] = 0x7F;
            elf[1] = (byte)'E';
            elf[2] = (byte)'L';
            elf[3] = (byte)'F';
            elf[4] = 2; // ELFCLASS64
            elf[5] = 1; // ELFDATA2LSB (Little Endian)
            elf[6] = 1; // EV_CURRENT
            elf[7] = 0; // ELFOSABI_SYSV
            // elf[8..15] = 0

            Buffer.BlockCopy(BitConverter.GetBytes((short)2), 0, elf, 16, 2);  // e_type = ET_EXEC
            Buffer.BlockCopy(BitConverter.GetBytes((short)0x3E), 0, elf, 18, 2); // e_machine = EM_X86_64
            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, elf, 20, 4);          // e_version = 1
            Buffer.BlockCopy(BitConverter.GetBytes(entryAddr), 0, elf, 24, 8);  // e_entry
            Buffer.BlockCopy(BitConverter.GetBytes((long)64), 0, elf, 32, 8);   // e_phoff = 64
            Buffer.BlockCopy(BitConverter.GetBytes((long)0), 0, elf, 40, 8);    // e_shoff = 0
            Buffer.BlockCopy(BitConverter.GetBytes(0), 0, elf, 48, 4);          // e_flags = 0
            Buffer.BlockCopy(BitConverter.GetBytes((short)64), 0, elf, 52, 2);  // e_ehsize = 64
            Buffer.BlockCopy(BitConverter.GetBytes((short)56), 0, elf, 54, 2);  // e_phentsize = 56
            Buffer.BlockCopy(BitConverter.GetBytes((short)numPhdrs), 0, elf, 56, 2); // e_phnum
            Buffer.BlockCopy(BitConverter.GetBytes((short)0), 0, elf, 58, 2);   // e_shentsize = 0
            Buffer.BlockCopy(BitConverter.GetBytes((short)0), 0, elf, 60, 2);   // e_shnum = 0
            Buffer.BlockCopy(BitConverter.GetBytes((short)0), 0, elf, 62, 2);   // e_shstrndx = 0

            // 2. Program Header 1: .text (PT_LOAD, RX)
            int ph1 = 64;
            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, elf, ph1 + 0, 4);           // p_type = PT_LOAD (1)
            Buffer.BlockCopy(BitConverter.GetBytes(5), 0, elf, ph1 + 4, 4);           // p_flags = PF_R | PF_X (1 | 4 = 5)
            Buffer.BlockCopy(BitConverter.GetBytes((long)textFileOffset), 0, elf, ph1 + 8, 8); // p_offset
            Buffer.BlockCopy(BitConverter.GetBytes(textVAddr), 0, elf, ph1 + 16, 8);   // p_vaddr
            Buffer.BlockCopy(BitConverter.GetBytes(textVAddr), 0, elf, ph1 + 24, 8);   // p_paddr
            Buffer.BlockCopy(BitConverter.GetBytes((long)rawTextSize), 0, elf, ph1 + 32, 8); // p_filesz
            Buffer.BlockCopy(BitConverter.GetBytes((long)rawTextSize), 0, elf, ph1 + 40, 8); // p_memsz
            Buffer.BlockCopy(BitConverter.GetBytes((long)pageSize), 0, elf, ph1 + 48, 8);    // p_align

            // 3. Program Header 2: .data / .bss (PT_LOAD, RW)
            if (hasData)
            {
                int ph2 = ph1 + 56;
                Buffer.BlockCopy(BitConverter.GetBytes(1), 0, elf, ph2 + 0, 4);           // p_type = PT_LOAD (1)
                Buffer.BlockCopy(BitConverter.GetBytes(6), 0, elf, ph2 + 4, 4);           // p_flags = PF_R | PF_W (4 | 2 = 6)
                Buffer.BlockCopy(BitConverter.GetBytes((long)dataFileOffset), 0, elf, ph2 + 8, 8); // p_offset
                Buffer.BlockCopy(BitConverter.GetBytes(dataVAddr), 0, elf, ph2 + 16, 8);   // p_vaddr
                Buffer.BlockCopy(BitConverter.GetBytes(dataVAddr), 0, elf, ph2 + 24, 8);   // p_paddr
                Buffer.BlockCopy(BitConverter.GetBytes((long)rawDataSize), 0, elf, ph2 + 32, 8); // p_filesz
                Buffer.BlockCopy(BitConverter.GetBytes((long)totalDataMemSize), 0, elf, ph2 + 40, 8); // p_memsz
                Buffer.BlockCopy(BitConverter.GetBytes((long)pageSize), 0, elf, ph2 + 48, 8);    // p_align
            }

            // Copy Code & Data
            prog.TextBytes.CopyTo(elf, textFileOffset);
            if (hasData && rawDataSize > 0)
            {
                prog.DataBytes.CopyTo(elf, dataFileOffset);
            }

            // Write output file
            string outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }
            File.WriteAllBytes(outputPath, elf);
        }
    }
}
