using System;
using System.Collections.Generic;
using System.IO;

namespace RawX
{
    public class FlatBinBuilder
    {
        public static void Build(EncodedProgram prog, string outputPath)
        {
            int textOffset = 0;
            int dataOffset = prog.TextBytes.Count;

            // Resolve Relocations
            foreach (var reloc in prog.Relocations)
            {
                int nextInstOffset = reloc.Offset + 4;
                int currentVAddr = textOffset + nextInstOffset;

                if (reloc.Type == "REL32")
                {
                    if (prog.Symbols.ContainsKey(reloc.TargetSymbol))
                    {
                        SymbolLocation sym = prog.Symbols[reloc.TargetSymbol];
                        int targetVAddr = (sym.Section == ".text") ? (textOffset + sym.Offset) : (dataOffset + sym.Offset);
                        int delta = targetVAddr - currentVAddr;
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
                        int targetVAddr = (sym.Section == ".text") ? (textOffset + sym.Offset) : (dataOffset + sym.Offset);
                        int delta = (targetVAddr - currentVAddr) + reloc.Addend;
                        byte[] deltaBytes = BitConverter.GetBytes(delta);
                        for (int b = 0; b < 4; b++)
                        {
                            prog.TextBytes[reloc.Offset + b] = deltaBytes[b];
                        }
                    }
                }
            }

            List<byte> raw = new List<byte>();
            raw.AddRange(prog.TextBytes);
            raw.AddRange(prog.DataBytes);

            string outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }
            File.WriteAllBytes(outputPath, raw.ToArray());
        }
    }
}
