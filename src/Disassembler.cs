using System;
using System.Collections.Generic;
using System.Text;

namespace RawX
{
    public class Disassembler
    {
        public static string Dump(EncodedProgram prog)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("========================================================");
            sb.AppendLine(" [RawX Disassembly Inspector] 64-bit AMD64 Machine Code");
            sb.AppendLine("========================================================");

            // Invert symbols for annotation
            Dictionary<int, string> textSyms = new Dictionary<int, string>();
            foreach (var kvp in prog.Symbols)
            {
                if (kvp.Value.Section == ".text")
                {
                    textSyms[kvp.Value.Offset] = kvp.Key;
                }
            }

            sb.AppendLine("--- .TEXT Section (" + prog.TextBytes.Count + " bytes) ---");
            byte[] code = prog.TextBytes.ToArray();
            for (int i = 0; i < code.Length; i += 16)
            {
                if (textSyms.ContainsKey(i))
                {
                    sb.AppendLine(string.Format("<{0}>:", textSyms[i]));
                }

                StringBuilder hex = new StringBuilder();
                StringBuilder ascii = new StringBuilder();
                int count = Math.Min(16, code.Length - i);

                for (int j = 0; j < count; j++)
                {
                    byte b = code[i + j];
                    hex.AppendFormat("{0:X2} ", b);
                    ascii.Append((b >= 32 && b <= 126) ? (char)b : '.');
                }

                sb.AppendLine(string.Format("  0x{0:X4}:  {1,-48}  |{2}|", i, hex.ToString(), ascii.ToString()));
            }

            if (prog.DataBytes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("--- .DATA Section (" + prog.DataBytes.Count + " bytes) ---");
                byte[] data = prog.DataBytes.ToArray();
                for (int i = 0; i < data.Length; i += 16)
                {
                    StringBuilder hex = new StringBuilder();
                    StringBuilder ascii = new StringBuilder();
                    int count = Math.Min(16, data.Length - i);

                    for (int j = 0; j < count; j++)
                    {
                        byte b = data[i + j];
                        hex.AppendFormat("{0:X2} ", b);
                        ascii.Append((b >= 32 && b <= 126) ? (char)b : '.');
                    }

                    sb.AppendLine(string.Format("  0x{0:X4}:  {1,-48}  |{2}|", i, hex.ToString(), ascii.ToString()));
                }
            }

            if (prog.BssSize > 0)
            {
                sb.AppendLine();
                sb.AppendLine(string.Format("--- .BSS Section (Reserved Uninitialized: {0} bytes) ---", prog.BssSize));
            }

            sb.AppendLine("========================================================");
            return sb.ToString();
        }
    }
}
