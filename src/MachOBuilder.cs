using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RawX
{
    public class MachOBuilder
    {
        public static int AlignUp(int val, int align)
        {
            return (val + align - 1) & ~(align - 1);
        }

        public static void Build(EncodedProgram prog, string outputPath)
        {
            const long pageZeroSize = 0x100000000L; // 4GB __PAGEZERO
            const long textBaseVAddr = 0x100000000L;
            const int pageSize = 0x1000; // 4096

            bool hasData = (prog.DataBytes.Count + prog.BssSize) > 0;

            int rawTextSize = Math.Max(1, prog.TextBytes.Count);
            int textFileOffset = pageSize; // code placed at offset 0x1000
            long textVAddr = textBaseVAddr + textFileOffset;
            int alignedTextSize = AlignUp(textFileOffset + rawTextSize, pageSize);

            int dataFileOffset = alignedTextSize;
            long dataVAddr = textBaseVAddr + dataFileOffset;
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
            else if (prog.Symbols.ContainsKey("main"))
            {
                entryAddr = textVAddr + prog.Symbols["main"].Offset;
            }
            else if (prog.Symbols.ContainsKey("_start"))
            {
                entryAddr = textVAddr + prog.Symbols["_start"].Offset;
            }

            // Commands:
            // 1. LC_SEGMENT_64 (__PAGEZERO): 72 bytes
            // 2. LC_SEGMENT_64 (__TEXT): 72 + 80 = 152 bytes
            // 3. LC_SEGMENT_64 (__DATA) if present: 72 + 80 = 152 bytes
            // 4. LC_UNIXTHREAD: 16 + 168 = 184 bytes
            int ncmds = hasData ? 4 : 3;
            int sizeofcmds = 72 + 152 + (hasData ? 152 : 0) + 184;

            int totalFileSize = hasData ? (dataFileOffset + rawDataSize) : (textFileOffset + rawTextSize);
            byte[] macho = new byte[totalFileSize];

            // 1. Mach Header (32 bytes)
            Buffer.BlockCopy(BitConverter.GetBytes(0xFEEDFACFU), 0, macho, 0, 4); // MH_MAGIC_64
            Buffer.BlockCopy(BitConverter.GetBytes(0x01000007), 0, macho, 4, 4); // CPU_TYPE_X86_64
            Buffer.BlockCopy(BitConverter.GetBytes(0x00000003), 0, macho, 8, 4); // CPU_SUBTYPE_X86_64_ALL
            Buffer.BlockCopy(BitConverter.GetBytes(2), 0, macho, 12, 4);          // MH_EXECUTE
            Buffer.BlockCopy(BitConverter.GetBytes(ncmds), 0, macho, 16, 4);      // ncmds
            Buffer.BlockCopy(BitConverter.GetBytes(sizeofcmds), 0, macho, 20, 4); // sizeofcmds
            Buffer.BlockCopy(BitConverter.GetBytes(0x00200085), 0, macho, 24, 4); // flags
            Buffer.BlockCopy(BitConverter.GetBytes(0), 0, macho, 28, 4);          // reserved

            int cmdOff = 32;

            // Load Command 1: __PAGEZERO (72 bytes)
            Buffer.BlockCopy(BitConverter.GetBytes(0x19), 0, macho, cmdOff + 0, 4);   // LC_SEGMENT_64
            Buffer.BlockCopy(BitConverter.GetBytes(72), 0, macho, cmdOff + 4, 4);     // cmdsize
            byte[] pzeroName = Encoding.ASCII.GetBytes("__PAGEZERO\0");
            Buffer.BlockCopy(pzeroName, 0, macho, cmdOff + 8, pzeroName.Length);
            Buffer.BlockCopy(BitConverter.GetBytes((long)0), 0, macho, cmdOff + 24, 8); // vmaddr = 0
            Buffer.BlockCopy(BitConverter.GetBytes(pageZeroSize), 0, macho, cmdOff + 32, 8); // vmsize = 4GB
            cmdOff += 72;

            // Load Command 2: __TEXT (152 bytes)
            Buffer.BlockCopy(BitConverter.GetBytes(0x19), 0, macho, cmdOff + 0, 4);   // LC_SEGMENT_64
            Buffer.BlockCopy(BitConverter.GetBytes(152), 0, macho, cmdOff + 4, 4);    // cmdsize = 72 + 80
            byte[] textSegName = Encoding.ASCII.GetBytes("__TEXT\0");
            Buffer.BlockCopy(textSegName, 0, macho, cmdOff + 8, textSegName.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(textBaseVAddr), 0, macho, cmdOff + 24, 8); // vmaddr
            Buffer.BlockCopy(BitConverter.GetBytes((long)alignedTextSize), 0, macho, cmdOff + 32, 8); // vmsize
            Buffer.BlockCopy(BitConverter.GetBytes((long)0), 0, macho, cmdOff + 40, 8); // fileoff
            Buffer.BlockCopy(BitConverter.GetBytes((long)alignedTextSize), 0, macho, cmdOff + 48, 8); // filesize
            Buffer.BlockCopy(BitConverter.GetBytes(7), 0, macho, cmdOff + 56, 4);     // maxprot = RWX
            Buffer.BlockCopy(BitConverter.GetBytes(5), 0, macho, cmdOff + 60, 4);     // initprot = RX
            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, macho, cmdOff + 64, 4);     // nsects = 1

            // Section __text (80 bytes at cmdOff + 72)
            int sectTextOff = cmdOff + 72;
            byte[] textSectName = Encoding.ASCII.GetBytes("__text\0");
            Buffer.BlockCopy(textSectName, 0, macho, sectTextOff + 0, textSectName.Length);
            Buffer.BlockCopy(textSegName, 0, macho, sectTextOff + 16, textSegName.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(textVAddr), 0, macho, sectTextOff + 32, 8); // addr
            Buffer.BlockCopy(BitConverter.GetBytes((long)rawTextSize), 0, macho, sectTextOff + 40, 8); // size
            Buffer.BlockCopy(BitConverter.GetBytes(textFileOffset), 0, macho, sectTextOff + 48, 4); // offset
            Buffer.BlockCopy(BitConverter.GetBytes(4), 0, macho, sectTextOff + 52, 4); // align (2^4 = 16)
            Buffer.BlockCopy(BitConverter.GetBytes(0x80000400), 0, macho, sectTextOff + 64, 4); // flags
            cmdOff += 152;

            // Load Command 3: __DATA (if present)
            if (hasData)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(0x19), 0, macho, cmdOff + 0, 4);   // LC_SEGMENT_64
                Buffer.BlockCopy(BitConverter.GetBytes(152), 0, macho, cmdOff + 4, 4);    // cmdsize = 72 + 80
                byte[] dataSegName = Encoding.ASCII.GetBytes("__DATA\0");
                Buffer.BlockCopy(dataSegName, 0, macho, cmdOff + 8, dataSegName.Length);
                Buffer.BlockCopy(BitConverter.GetBytes(dataVAddr), 0, macho, cmdOff + 24, 8); // vmaddr
                Buffer.BlockCopy(BitConverter.GetBytes((long)AlignUp(totalDataMemSize, pageSize)), 0, macho, cmdOff + 32, 8);
                Buffer.BlockCopy(BitConverter.GetBytes((long)dataFileOffset), 0, macho, cmdOff + 40, 8);
                Buffer.BlockCopy(BitConverter.GetBytes((long)rawDataSize), 0, macho, cmdOff + 48, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(7), 0, macho, cmdOff + 56, 4);     // maxprot = RWX
                Buffer.BlockCopy(BitConverter.GetBytes(3), 0, macho, cmdOff + 60, 4);     // initprot = RW
                Buffer.BlockCopy(BitConverter.GetBytes(1), 0, macho, cmdOff + 64, 4);     // nsects = 1

                // Section __data
                int sectDataOff = cmdOff + 72;
                byte[] dataSectName = Encoding.ASCII.GetBytes("__data\0");
                Buffer.BlockCopy(dataSectName, 0, macho, sectDataOff + 0, dataSectName.Length);
                Buffer.BlockCopy(dataSegName, 0, macho, sectDataOff + 16, dataSegName.Length);
                Buffer.BlockCopy(BitConverter.GetBytes(dataVAddr), 0, macho, sectDataOff + 32, 8);
                Buffer.BlockCopy(BitConverter.GetBytes((long)rawDataSize), 0, macho, sectDataOff + 40, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(dataFileOffset), 0, macho, sectDataOff + 48, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(3), 0, macho, sectDataOff + 52, 4); // align (8)
                Buffer.BlockCopy(BitConverter.GetBytes(0), 0, macho, sectDataOff + 64, 4);
                cmdOff += 152;
            }

            // Load Command: LC_UNIXTHREAD (184 bytes)
            Buffer.BlockCopy(BitConverter.GetBytes(0x5), 0, macho, cmdOff + 0, 4);    // LC_UNIXTHREAD
            Buffer.BlockCopy(BitConverter.GetBytes(184), 0, macho, cmdOff + 4, 4);    // cmdsize = 16 + 168
            Buffer.BlockCopy(BitConverter.GetBytes(4), 0, macho, cmdOff + 8, 4);      // flavor = x86_THREAD_STATE64
            Buffer.BlockCopy(BitConverter.GetBytes(42), 0, macho, cmdOff + 12, 4);    // count = 168 / 4 = 42

            // Thread state starts at cmdOff + 16.
            // rip is at offset 16 * 8 = 128 within the state struct:
            Buffer.BlockCopy(BitConverter.GetBytes(entryAddr), 0, macho, cmdOff + 16 + 128, 8);

            // Copy Code and Data
            prog.TextBytes.CopyTo(macho, textFileOffset);
            if (hasData && rawDataSize > 0)
            {
                prog.DataBytes.CopyTo(macho, dataFileOffset);
            }

            // Write output
            string outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }
            File.WriteAllBytes(outputPath, macho);
        }
    }
}
