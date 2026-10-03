using System;
using System.Collections.Generic;
using System.Text;

namespace RawX
{
    public class Encoder
    {
        public static readonly HashSet<string> KnownKernel32 = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ExitProcess", "GetStdHandle", "WriteFile", "WriteConsoleA", "ReadFile",
            "CreateFileA", "CloseHandle", "GetCommandLineA", "Sleep", "GetTickCount64",
            "VirtualAlloc", "VirtualFree", "HeapAlloc", "HeapFree", "GetProcessHeap"
        };

        private TargetPlatform target;

        public Encoder(TargetPlatform targetPlatform = TargetPlatform.Windows)
        {
            target = targetPlatform;
        }

        public byte MakeRex(int w, int r, int x, int b)
        {
            return (byte)(0x40 | ((w & 1) << 3) | ((r & 1) << 2) | ((x & 1) << 1) | (b & 1));
        }

        public byte MakeModRm(int mod, int reg, int rm)
        {
            return (byte)(((mod & 3) << 6) | ((reg & 7) << 3) | (rm & 7));
        }

        public byte MakeSib(int scale, int index, int baseReg)
        {
            int scaleCode = 0;
            if (scale == 2) scaleCode = 1;
            else if (scale == 4) scaleCode = 2;
            else if (scale == 8) scaleCode = 3;

            return (byte)(((scaleCode & 3) << 6) | ((index & 7) << 3) | (baseReg & 7));
        }

        public EncodedProgram Encode(ProgramAST ast)
        {
            EncodedProgram prog = new EncodedProgram();
            prog.EntrySymbol = ast.EntrySymbol;

            string currentSection = ".text";

            foreach (Statement stmt in ast.Statements)
            {
                if (!string.IsNullOrEmpty(stmt.Directive))
                {
                    string dir = stmt.Directive.ToLowerInvariant();
                    if (dir == "section")
                    {
                        if (stmt.DirectiveArgs.Count > 0)
                        {
                            currentSection = stmt.DirectiveArgs[0].ToString().ToLowerInvariant();
                        }
                    }
                    else if (dir == "entry" || dir == "global")
                    {
                        if (stmt.DirectiveArgs.Count > 0)
                        {
                            prog.EntrySymbol = stmt.DirectiveArgs[0].ToString();
                        }
                    }
                    else if (dir == "align")
                    {
                        if (stmt.DirectiveArgs.Count > 0)
                        {
                            long align = Convert.ToInt64(stmt.DirectiveArgs[0]);
                            if (align > 1)
                            {
                                List<byte> targetBuf = (currentSection == ".text") ? prog.TextBytes : prog.DataBytes;
                                while ((targetBuf.Count % align) != 0)
                                {
                                    targetBuf.Add(currentSection == ".text" ? (byte)0x90 : (byte)0x00);
                                }
                            }
                        }
                    }
                    else if (dir == "db" || dir == "dw" || dir == "dd" || dir == "dq")
                    {
                        if (!string.IsNullOrEmpty(stmt.Label))
                        {
                            prog.Symbols[stmt.Label] = new SymbolLocation(".data", prog.DataBytes.Count);
                        }

                        foreach (object arg in stmt.DirectiveArgs)
                        {
                            if (arg is string)
                            {
                                byte[] bytes = Encoding.ASCII.GetBytes((string)arg);
                                prog.DataBytes.AddRange(bytes);
                            }
                            else if (arg is long || arg is int)
                            {
                                long val = Convert.ToInt64(arg);
                                if (dir == "db") prog.DataBytes.Add((byte)(val & 0xFF));
                                else if (dir == "dw") prog.DataBytes.AddRange(BitConverter.GetBytes((short)val));
                                else if (dir == "dd") prog.DataBytes.AddRange(BitConverter.GetBytes((int)val));
                                else if (dir == "dq") prog.DataBytes.AddRange(BitConverter.GetBytes(val));
                            }
                        }
                    }
                    else if (dir == "resb" || dir == "resw" || dir == "resd" || dir == "resq")
                    {
                        long count = 1;
                        if (stmt.DirectiveArgs.Count > 0)
                        {
                            count = Convert.ToInt64(stmt.DirectiveArgs[0]);
                        }
                        int unit = 1;
                        if (dir == "resw") unit = 2;
                        else if (dir == "resd") unit = 4;
                        else if (dir == "resq") unit = 8;

                        int totalBytes = (int)(count * unit);
                        if (!string.IsNullOrEmpty(stmt.Label))
                        {
                            prog.Symbols[stmt.Label] = new SymbolLocation(".bss", prog.BssSize);
                        }
                        prog.BssSize += totalBytes;
                    }
                    continue;
                }

                // If label only
                if (!string.IsNullOrEmpty(stmt.Label) && string.IsNullOrEmpty(stmt.Mnemonic))
                {
                    int offset = (currentSection == ".text") ? prog.TextBytes.Count : prog.DataBytes.Count;
                    prog.Symbols[stmt.Label] = new SymbolLocation(currentSection, offset);
                    continue;
                }

                // Instruction
                if (!string.IsNullOrEmpty(stmt.Mnemonic))
                {
                    if (!string.IsNullOrEmpty(stmt.Label))
                    {
                        prog.Symbols[stmt.Label] = new SymbolLocation(".text", prog.TextBytes.Count);
                    }
                    EncodeInstruction(stmt, prog);
                }
            }

            return prog;
        }

        public void EncodeMemAccess(List<byte> buf, EncodedProgram prog, byte opcode, int regIdx, MemoryOperand mem, bool is64 = true, byte mandatoryPrefix = 0, bool has0FEscape = false)
        {
            if (mandatoryPrefix != 0) buf.Add(mandatoryPrefix);

            // RIP-Relative
            if (!string.IsNullOrEmpty(mem.Label) && mem.BaseReg == null && mem.IndexReg == null)
            {
                int r = (regIdx >= 8) ? 1 : 0;
                int w = is64 ? 1 : 0;
                if (w == 1 || r == 1)
                {
                    buf.Add(MakeRex(w, r, 0, 0));
                }
                if (has0FEscape) buf.Add(0x0F);
                buf.Add(opcode);
                buf.Add(MakeModRm(0, regIdx & 7, 5)); // Mod 00, Reg, RM 101 (RIP-rel)

                Relocation reloc = new Relocation
                {
                    Offset = buf.Count,
                    TargetSymbol = mem.Label,
                    Type = "RIP_REL32",
                    Addend = (int)mem.Disp
                };
                prog.Relocations.Add(reloc);
                buf.AddRange(new byte[4]);
                return;
            }

            int baseIdx = mem.BaseReg != null ? Parser.RegMap[mem.BaseReg].Index : -1;
            int indexIdx = mem.IndexReg != null ? Parser.RegMap[mem.IndexReg].Index : -1;

            int rexW = is64 ? 1 : 0;
            int rexR = (regIdx >= 8) ? 1 : 0;
            int rexX = (indexIdx >= 8) ? 1 : 0;
            int rexB = (baseIdx >= 8) ? 1 : 0;

            if (rexW == 1 || rexR == 1 || rexX == 1 || rexB == 1)
            {
                buf.Add(MakeRex(rexW, rexR, rexX, rexB));
            }

            if (has0FEscape) buf.Add(0x0F);
            buf.Add(opcode);

            // Determine displacement size
            int mod = 0;
            if (mem.Disp != 0 || (baseIdx & 7) == 5) // RBP/R13 requires disp
            {
                if (mem.Disp >= -128 && mem.Disp <= 127)
                {
                    mod = 1; // 8-bit disp
                }
                else
                {
                    mod = 2; // 32-bit disp
                }
            }

            bool needsSib = (indexIdx != -1) || ((baseIdx & 7) == 4); // RSP/R12 requires SIB

            if (needsSib)
            {
                buf.Add(MakeModRm(mod, regIdx & 7, 4)); // RM = 4 indicates SIB follows
                int sIndex = (indexIdx != -1) ? (indexIdx & 7) : 4; // 4 = no index
                int sBase = (baseIdx != -1) ? (baseIdx & 7) : 5;   // 5 = no base
                buf.Add(MakeSib(mem.Scale, sIndex, sBase));
            }
            else
            {
                buf.Add(MakeModRm(mod, regIdx & 7, baseIdx & 7));
            }

            // Emit displacement
            if (mod == 1)
            {
                buf.Add((byte)((sbyte)mem.Disp));
            }
            else if (mod == 2 || (mem.Disp == 0 && (baseIdx & 7) == 5))
            {
                buf.AddRange(BitConverter.GetBytes((int)mem.Disp));
            }
        }

        public void EncodeInstruction(Statement s, EncodedProgram prog)
        {
            string m = s.Mnemonic.ToLowerInvariant();
            List<Operand> ops = s.Operands;
            List<byte> buf = prog.TextBytes;

            // Handle lock prefix
            if (m.StartsWith("lock "))
            {
                buf.Add(0xF0);
                m = m.Substring(5).Trim();
            }

            // NOP
            if (m == "nop") { buf.Add(0x90); return; }

            // RET
            if (m == "ret") { buf.Add(0xC3); return; }

            // SYSCALL
            if (m == "syscall") { buf.AddRange(new byte[] { 0x0F, 0x05 }); return; }

            // Hardware Intrinsics
            if (m == "rdtsc") { buf.AddRange(new byte[] { 0x0F, 0x31 }); return; }
            if (m == "rdtscp") { buf.AddRange(new byte[] { 0x0F, 0x01, 0xF9 }); return; }
            if (m == "cpuid") { buf.AddRange(new byte[] { 0x0F, 0xA2 }); return; }
            if (m == "pause") { buf.AddRange(new byte[] { 0xF3, 0x90 }); return; }
            if (m == "int3") { buf.Add(0xCC); return; }
            if (m == "leave") { buf.Add(0xC9); return; }
            if (m == "cqo") { buf.AddRange(new byte[] { 0x48, 0x99 }); return; }
            if (m == "cdq") { buf.Add(0x99); return; }

            // PUSH
            if (m == "push" && ops.Count == 1)
            {
                if (ops[0].Type == OperandType.Register)
                {
                    int idx = ops[0].RegIndex;
                    if (idx >= 8) buf.Add(MakeRex(0, 0, 0, 1));
                    buf.Add((byte)(0x50 + (idx & 7)));
                    return;
                }
                else if (ops[0].Type == OperandType.Immediate)
                {
                    long imm = ops[0].Imm;
                    if (imm >= -128 && imm <= 127)
                    {
                        buf.Add(0x6A);
                        buf.Add((byte)((sbyte)imm));
                    }
                    else
                    {
                        buf.Add(0x68);
                        buf.AddRange(BitConverter.GetBytes((int)imm));
                    }
                    return;
                }
            }

            // POP
            if (m == "pop" && ops.Count == 1 && ops[0].Type == OperandType.Register)
            {
                int idx = ops[0].RegIndex;
                if (idx >= 8) buf.Add(MakeRex(0, 0, 0, 1));
                buf.Add((byte)(0x58 + (idx & 7)));
                return;
            }

            // Branch Instructions (JMP and Jcc)
            Dictionary<string, byte[]> branches = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "jmp", new byte[] { 0xE9 } },
                { "je",  new byte[] { 0x0F, 0x84 } }, { "jz",  new byte[] { 0x0F, 0x84 } },
                { "jne", new byte[] { 0x0F, 0x85 } }, { "jnz", new byte[] { 0x0F, 0x85 } },
                { "jl",  new byte[] { 0x0F, 0x8C } }, { "jnge", new byte[] { 0x0F, 0x8C } },
                { "jle", new byte[] { 0x0F, 0x8E } }, { "jng",  new byte[] { 0x0F, 0x8E } },
                { "jg",  new byte[] { 0x0F, 0x8F } }, { "jnle", new byte[] { 0x0F, 0x8F } },
                { "jge", new byte[] { 0x0F, 0x8D } }, { "jnl",  new byte[] { 0x0F, 0x8D } },
                { "jb",  new byte[] { 0x0F, 0x82 } }, { "jnae", new byte[] { 0x0F, 0x82 } }, { "jc", new byte[] { 0x0F, 0x82 } },
                { "jbe", new byte[] { 0x0F, 0x86 } }, { "jna",  new byte[] { 0x0F, 0x86 } },
                { "ja",  new byte[] { 0x0F, 0x87 } }, { "jnbe", new byte[] { 0x0F, 0x87 } },
                { "jae", new byte[] { 0x0F, 0x83 } }, { "jnb",  new byte[] { 0x0F, 0x83 } }, { "jnc", new byte[] { 0x0F, 0x83 } },
                { "call", new byte[] { 0xE8 } }
            };

            if (branches.ContainsKey(m) && ops.Count == 1 && ops[0].Type == OperandType.Label)
            {
                string targetSym = ops[0].Label;
                // Win64 import call check
                if (m == "call" && target == TargetPlatform.Windows &&
                    (KnownKernel32.Contains(targetSym) || targetSym.StartsWith("Win", StringComparison.OrdinalIgnoreCase)))
                {
                    buf.AddRange(new byte[] { 0xFF, 0x15 }); // call qword [rip + offset]
                    prog.Relocations.Add(new Relocation
                    {
                        Offset = buf.Count,
                        TargetSymbol = targetSym,
                        Type = "IMPORT_CALL"
                    });
                    buf.AddRange(new byte[4]);
                    return;
                }

                buf.AddRange(branches[m]);
                prog.Relocations.Add(new Relocation
                {
                    Offset = buf.Count,
                    TargetSymbol = targetSym,
                    Type = "REL32"
                });
                buf.AddRange(new byte[4]);
                return;
            }

            // CALL REG: call rax, call rbx, etc.
            if (m == "call" && ops.Count == 1 && ops[0].Type == OperandType.Register)
            {
                int rIdx = ops[0].RegIndex;
                if (rIdx >= 8) buf.Add(MakeRex(0, 0, 0, 1));
                buf.Add(0xFF);
                buf.Add(MakeModRm(3, 2, rIdx & 7)); // FF /2
                return;
            }

            // LEA dst, [mem]
            if (m == "lea" && ops.Count == 2 && ops[0].Type == OperandType.Register && ops[1].Type == OperandType.Memory)
            {
                EncodeMemAccess(buf, prog, 0x8D, ops[0].RegIndex, ops[1].Mem, true);
                return;
            }

            // MOV
            if (m == "mov" && ops.Count == 2)
            {
                Operand op1 = ops[0];
                Operand op2 = ops[1];

                // mov reg, reg
                if (op1.Type == OperandType.Register && op2.Type == OperandType.Register)
                {
                    int dst = op1.RegIndex;
                    int src = op2.RegIndex;
                    int sz = op1.RegSize;

                    if (sz == 64)
                    {
                        buf.Add(MakeRex(1, src >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0));
                        buf.Add(0x89);
                        buf.Add(MakeModRm(3, src & 7, dst & 7));
                    }
                    else if (sz == 32)
                    {
                        if (src >= 8 || dst >= 8) buf.Add(MakeRex(0, src >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0));
                        buf.Add(0x89);
                        buf.Add(MakeModRm(3, src & 7, dst & 7));
                    }
                    else if (sz == 16)
                    {
                        buf.Add(0x66);
                        if (src >= 8 || dst >= 8) buf.Add(MakeRex(0, src >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0));
                        buf.Add(0x89);
                        buf.Add(MakeModRm(3, src & 7, dst & 7));
                    }
                    else if (sz == 8)
                    {
                        bool needsRex = src >= 4 || dst >= 4;
                        if (needsRex) buf.Add(MakeRex(0, src >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0));
                        buf.Add(0x88);
                        buf.Add(MakeModRm(3, src & 7, dst & 7));
                    }
                    return;
                }

                // mov reg, imm
                if (op1.Type == OperandType.Register && op2.Type == OperandType.Immediate)
                {
                    int r = op1.RegIndex;
                    int sz = op1.RegSize;
                    long imm = op2.Imm;

                    if (sz == 64)
                    {
                        if (imm >= 0 && imm <= 0xFFFFFFFFL)
                        {
                            if (r >= 8) buf.Add(MakeRex(0, 0, 0, 1));
                            buf.Add((byte)(0xB8 + (r & 7)));
                            buf.AddRange(BitConverter.GetBytes((int)imm));
                        }
                        else if (imm >= int.MinValue && imm <= int.MaxValue)
                        {
                            buf.Add(MakeRex(1, 0, 0, r >= 8 ? 1 : 0));
                            buf.Add(0xC7);
                            buf.Add(MakeModRm(3, 0, r & 7));
                            buf.AddRange(BitConverter.GetBytes((int)imm));
                        }
                        else
                        {
                            buf.Add(MakeRex(1, 0, 0, r >= 8 ? 1 : 0));
                            buf.Add((byte)(0xB8 + (r & 7)));
                            buf.AddRange(BitConverter.GetBytes(imm));
                        }
                    }
                    else if (sz == 32)
                    {
                        if (r >= 8) buf.Add(MakeRex(0, 0, 0, 1));
                        buf.Add((byte)(0xB8 + (r & 7)));
                        buf.AddRange(BitConverter.GetBytes((int)imm));
                    }
                    else if (sz == 16)
                    {
                        buf.Add(0x66);
                        if (r >= 8) buf.Add(MakeRex(0, 0, 0, 1));
                        buf.Add((byte)(0xB8 + (r & 7)));
                        buf.AddRange(BitConverter.GetBytes((short)imm));
                    }
                    else if (sz == 8)
                    {
                        if (r >= 4) buf.Add(MakeRex(0, 0, 0, r >= 8 ? 1 : 0));
                        buf.Add((byte)(0xB0 + (r & 7)));
                        buf.Add((byte)(imm & 0xFF));
                    }
                    return;
                }

                // mov reg, [mem]
                if (op1.Type == OperandType.Register && op2.Type == OperandType.Memory)
                {
                    bool is64 = op1.RegSize == 64;
                    byte opc = op1.RegSize == 8 ? (byte)0x8A : (byte)0x8B;
                    byte pref = op1.RegSize == 16 ? (byte)0x66 : (byte)0;
                    EncodeMemAccess(buf, prog, opc, op1.RegIndex, op2.Mem, is64, pref);
                    return;
                }

                // mov [mem], reg
                if (op1.Type == OperandType.Memory && op2.Type == OperandType.Register)
                {
                    bool is64 = op2.RegSize == 64;
                    byte opc = op2.RegSize == 8 ? (byte)0x88 : (byte)0x89;
                    byte pref = op2.RegSize == 16 ? (byte)0x66 : (byte)0;
                    EncodeMemAccess(buf, prog, opc, op2.RegIndex, op1.Mem, is64, pref);
                    return;
                }

                // mov [mem], imm
                if (op1.Type == OperandType.Memory && op2.Type == OperandType.Immediate)
                {
                    bool is64 = op1.Mem.Size == 64;
                    byte opc = op1.Mem.Size == 8 ? (byte)0xC6 : (byte)0xC7;
                    EncodeMemAccess(buf, prog, opc, 0, op1.Mem, is64);
                    if (op1.Mem.Size == 8) buf.Add((byte)(op2.Imm & 0xFF));
                    else if (op1.Mem.Size == 16) buf.AddRange(BitConverter.GetBytes((short)op2.Imm));
                    else buf.AddRange(BitConverter.GetBytes((int)op2.Imm));
                    return;
                }
            }

            // ALU Operations: add, or, and, sub, xor, cmp, test
            Dictionary<string, int> aluExt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "add", 0 }, { "or", 1 }, { "and", 4 }, { "sub", 5 }, { "xor", 6 }, { "cmp", 7 }, { "test", 0 }
            };

            if (aluExt.ContainsKey(m) && ops.Count == 2)
            {
                Operand op1 = ops[0];
                Operand op2 = ops[1];
                int ext = aluExt[m];

                // reg, reg
                if (op1.Type == OperandType.Register && op2.Type == OperandType.Register)
                {
                    int dst = op1.RegIndex;
                    int src = op2.RegIndex;
                    int sz = op1.RegSize;

                    if (m == "test")
                    {
                        if (sz == 64) buf.Add(MakeRex(1, src >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0));
                        else if (src >= 8 || dst >= 8) buf.Add(MakeRex(0, src >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0));
                        buf.Add(sz == 8 ? (byte)0x84 : (byte)0x85);
                        buf.Add(MakeModRm(3, src & 7, dst & 7));
                    }
                    else
                    {
                        byte baseOpcode = (byte)(ext * 8 + 1);
                        if (sz == 64) buf.Add(MakeRex(1, src >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0));
                        else if (src >= 8 || dst >= 8) buf.Add(MakeRex(0, src >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0));
                        buf.Add(baseOpcode);
                        buf.Add(MakeModRm(3, src & 7, dst & 7));
                    }
                    return;
                }

                // reg, imm
                if (op1.Type == OperandType.Register && op2.Type == OperandType.Immediate)
                {
                    int dst = op1.RegIndex;
                    int sz = op1.RegSize;
                    long imm = op2.Imm;

                    if (m == "test")
                    {
                        if (sz == 64) buf.Add(MakeRex(1, 0, 0, dst >= 8 ? 1 : 0));
                        else if (dst >= 8) buf.Add(MakeRex(0, 0, 0, 1));
                        buf.Add(sz == 8 ? (byte)0xF6 : (byte)0xF7);
                        buf.Add(MakeModRm(3, 0, dst & 7));
                        if (sz == 8) buf.Add((byte)(imm & 0xFF));
                        else buf.AddRange(BitConverter.GetBytes((int)imm));
                    }
                    else
                    {
                        bool isShortImm = (imm >= -128 && imm <= 127);
                        if (sz == 64) buf.Add(MakeRex(1, 0, 0, dst >= 8 ? 1 : 0));
                        else if (dst >= 8) buf.Add(MakeRex(0, 0, 0, 1));

                        if (isShortImm)
                        {
                            buf.Add(0x83);
                            buf.Add(MakeModRm(3, ext, dst & 7));
                            buf.Add((byte)((sbyte)imm));
                        }
                        else
                        {
                            buf.Add(0x81);
                            buf.Add(MakeModRm(3, ext, dst & 7));
                            buf.AddRange(BitConverter.GetBytes((int)imm));
                        }
                    }
                    return;
                }

                // reg, [mem]
                if (op1.Type == OperandType.Register && op2.Type == OperandType.Memory)
                {
                    byte baseOpcode = (byte)(ext * 8 + 3);
                    EncodeMemAccess(buf, prog, baseOpcode, op1.RegIndex, op2.Mem, op1.RegSize == 64);
                    return;
                }

                // [mem], reg
                if (op1.Type == OperandType.Memory && op2.Type == OperandType.Register)
                {
                    byte baseOpcode = (byte)(ext * 8 + 1);
                    EncodeMemAccess(buf, prog, baseOpcode, op2.RegIndex, op1.Mem, op2.RegSize == 64);
                    return;
                }
            }

            // Single Operand: inc, dec, not, neg, idiv
            Dictionary<string, int> unaryExt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "inc", 0 }, { "dec", 1 }, { "not", 2 }, { "neg", 3 }, { "idiv", 7 }
            };

            if (unaryExt.ContainsKey(m) && ops.Count == 1)
            {
                Operand op = ops[0];
                int ext = unaryExt[m];

                if (op.Type == OperandType.Register)
                {
                    int r = op.RegIndex;
                    int sz = op.RegSize;

                    if (sz == 64) buf.Add(MakeRex(1, 0, 0, r >= 8 ? 1 : 0));
                    else if (r >= 8) buf.Add(MakeRex(0, 0, 0, 1));

                    if (m == "inc" || m == "dec")
                    {
                        buf.Add(0xFF);
                        buf.Add(MakeModRm(3, ext, r & 7));
                    }
                    else
                    {
                        buf.Add(0xF7);
                        buf.Add(MakeModRm(3, ext, r & 7));
                    }
                    return;
                }
                else if (op.Type == OperandType.Memory)
                {
                    byte opc = (m == "inc" || m == "dec") ? (byte)0xFF : (byte)0xF7;
                    EncodeMemAccess(buf, prog, opc, ext, op.Mem, op.Mem.Size == 64);
                    return;
                }
            }

            // Shifts: shl, shr, sar
            Dictionary<string, int> shiftExt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "shl", 4 }, { "shr", 5 }, { "sar", 7 }
            };

            if (shiftExt.ContainsKey(m) && ops.Count == 2 && ops[0].Type == OperandType.Register)
            {
                int r = ops[0].RegIndex;
                int sz = ops[0].RegSize;
                int ext = shiftExt[m];

                if (ops[1].Type == OperandType.Immediate)
                {
                    long cnt = ops[1].Imm;
                    if (sz == 64) buf.Add(MakeRex(1, 0, 0, r >= 8 ? 1 : 0));
                    else if (r >= 8) buf.Add(MakeRex(0, 0, 0, 1));

                    if (cnt == 1)
                    {
                        buf.Add(0xD1);
                        buf.Add(MakeModRm(3, ext, r & 7));
                    }
                    else
                    {
                        buf.Add(0xC1);
                        buf.Add(MakeModRm(3, ext, r & 7));
                        buf.Add((byte)(cnt & 0xFF));
                    }
                    return;
                }
                else if (ops[1].Type == OperandType.Register && ops[1].Reg.Equals("cl", StringComparison.OrdinalIgnoreCase))
                {
                    if (sz == 64) buf.Add(MakeRex(1, 0, 0, r >= 8 ? 1 : 0));
                    else if (r >= 8) buf.Add(MakeRex(0, 0, 0, 1));
                    buf.Add(0xD3);
                    buf.Add(MakeModRm(3, ext, r & 7));
                    return;
                }
            }

            // Signed Multiply: imul reg, reg / imul reg, imm
            if (m == "imul")
            {
                if (ops.Count == 2 && ops[0].Type == OperandType.Register && ops[1].Type == OperandType.Register)
                {
                    int dst = ops[0].RegIndex;
                    int src = ops[1].RegIndex;
                    buf.Add(MakeRex(1, dst >= 8 ? 1 : 0, 0, src >= 8 ? 1 : 0));
                    buf.AddRange(new byte[] { 0x0F, 0xAF });
                    buf.Add(MakeModRm(3, dst & 7, src & 7));
                    return;
                }
                else if (ops.Count == 2 && ops[0].Type == OperandType.Register && ops[1].Type == OperandType.Immediate)
                {
                    int dst = ops[0].RegIndex;
                    long imm = ops[1].Imm;
                    buf.Add(MakeRex(1, dst >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0));
                    buf.Add(0x69);
                    buf.Add(MakeModRm(3, dst & 7, dst & 7));
                    buf.AddRange(BitConverter.GetBytes((int)imm));
                    return;
                }
            }

            // XCHG reg, reg / xchg reg, [mem]
            if (m == "xchg" && ops.Count == 2)
            {
                if (ops[0].Type == OperandType.Register && ops[1].Type == OperandType.Register)
                {
                    int r1 = ops[0].RegIndex;
                    int r2 = ops[1].RegIndex;
                    buf.Add(MakeRex(1, r1 >= 8 ? 1 : 0, 0, r2 >= 8 ? 1 : 0));
                    buf.Add(0x87);
                    buf.Add(MakeModRm(3, r1 & 7, r2 & 7));
                    return;
                }
                else if (ops[0].Type == OperandType.Memory && ops[1].Type == OperandType.Register)
                {
                    EncodeMemAccess(buf, prog, 0x87, ops[1].RegIndex, ops[0].Mem, true);
                    return;
                }
                else if (ops[0].Type == OperandType.Register && ops[1].Type == OperandType.Memory)
                {
                    EncodeMemAccess(buf, prog, 0x87, ops[0].RegIndex, ops[1].Mem, true);
                    return;
                }
            }

            // CMPXCHG [mem], reg or CMPXCHG reg, reg
            if (m == "cmpxchg" && ops.Count == 2)
            {
                if (ops[0].Type == OperandType.Memory && ops[1].Type == OperandType.Register)
                {
                    EncodeMemAccess(buf, prog, 0xB1, ops[1].RegIndex, ops[0].Mem, true, 0, true);
                    return;
                }
                else if (ops[0].Type == OperandType.Register && ops[1].Type == OperandType.Register)
                {
                    int dst = ops[0].RegIndex;
                    int src = ops[1].RegIndex;
                    buf.Add(MakeRex(1, src >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0));
                    buf.AddRange(new byte[] { 0x0F, 0xB1 });
                    buf.Add(MakeModRm(3, src & 7, dst & 7));
                    return;
                }
            }

            // SETcc reg8
            Dictionary<string, byte> setccMap = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
            {
                { "sete", 0x94 }, { "setz", 0x94 },
                { "setne", 0x95 }, { "setnz", 0x95 },
                { "setl", 0x9C }, { "setle", 0x9E },
                { "setg", 0x9F }, { "setge", 0x9D },
                { "setb", 0x92 }, { "setbe", 0x96 },
                { "seta", 0x97 }, { "setae", 0x93 }
            };

            if (setccMap.ContainsKey(m) && ops.Count == 1 && ops[0].Type == OperandType.Register)
            {
                int r = ops[0].RegIndex;
                if (r >= 4) buf.Add(MakeRex(0, 0, 0, r >= 8 ? 1 : 0));
                buf.AddRange(new byte[] { 0x0F, setccMap[m] });
                buf.Add(MakeModRm(3, 0, r & 7));
                return;
            }

            // CMOVcc reg, reg / CMOVcc reg, [mem]
            Dictionary<string, byte> cmovccMap = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
            {
                { "cmove", 0x44 }, { "cmovz", 0x44 },
                { "cmovne", 0x45 }, { "cmovnz", 0x45 },
                { "cmovl", 0x4C }, { "cmovle", 0x4E },
                { "cmovg", 0x4F }, { "cmovge", 0x4D },
                { "cmovb", 0x42 }, { "cmovbe", 0x46 },
                { "cmova", 0x47 }, { "cmovae", 0x43 }
            };

            if (cmovccMap.ContainsKey(m) && ops.Count == 2)
            {
                int dst = ops[0].RegIndex;
                byte opCode = cmovccMap[m];

                if (ops[1].Type == OperandType.Register)
                {
                    int src = ops[1].RegIndex;
                    buf.Add(MakeRex(1, dst >= 8 ? 1 : 0, 0, src >= 8 ? 1 : 0));
                    buf.AddRange(new byte[] { 0x0F, opCode });
                    buf.Add(MakeModRm(3, dst & 7, src & 7));
                    return;
                }
                else if (ops[1].Type == OperandType.Memory)
                {
                    EncodeMemAccess(buf, prog, opCode, dst, ops[1].Mem, true, 0, true);
                    return;
                }
            }

            // Bit Scan: BSF / BSR / POPCNT
            if ((m == "bsf" || m == "bsr" || m == "popcnt") && ops.Count == 2 && ops[0].Type == OperandType.Register)
            {
                int dst = ops[0].RegIndex;
                byte code = (m == "bsf") ? (byte)0xBC : ((m == "bsr") ? (byte)0xBD : (byte)0xB8);
                byte mandatory = (m == "popcnt") ? (byte)0xF3 : (byte)0;

                if (ops[1].Type == OperandType.Register)
                {
                    if (mandatory != 0) buf.Add(mandatory);
                    int src = ops[1].RegIndex;
                    buf.Add(MakeRex(1, dst >= 8 ? 1 : 0, 0, src >= 8 ? 1 : 0));
                    buf.AddRange(new byte[] { 0x0F, code });
                    buf.Add(MakeModRm(3, dst & 7, src & 7));
                    return;
                }
                else if (ops[1].Type == OperandType.Memory)
                {
                    EncodeMemAccess(buf, prog, code, dst, ops[1].Mem, true, mandatory, true);
                    return;
                }
            }

            // SIMD / SSE Instructions (XMM)
            // (mandatoryPrefix, opcodeByte)
            Dictionary<string, Tuple<byte, byte>> sseOpcodes = new Dictionary<string, Tuple<byte, byte>>(StringComparer.OrdinalIgnoreCase)
            {
                { "movups", Tuple.Create((byte)0x00, (byte)0x10) },
                { "movaps", Tuple.Create((byte)0x00, (byte)0x28) },
                { "xorps",  Tuple.Create((byte)0x00, (byte)0x57) },
                { "addps",  Tuple.Create((byte)0x00, (byte)0x58) },
                { "subps",  Tuple.Create((byte)0x00, (byte)0x5C) },
                { "mulps",  Tuple.Create((byte)0x00, (byte)0x59) },
                { "divps",  Tuple.Create((byte)0x00, (byte)0x5E) },
                { "pxor",   Tuple.Create((byte)0x66, (byte)0xEF) },
                { "movdqa", Tuple.Create((byte)0x66, (byte)0x6F) },
                { "movdqu", Tuple.Create((byte)0xF3, (byte)0x6F) }
            };

            if (sseOpcodes.ContainsKey(m) && ops.Count == 2)
            {
                var info = sseOpcodes[m];
                byte mandPref = info.Item1;
                byte baseCode = info.Item2;

                // xmm, xmm
                if (ops[0].Type == OperandType.Register && ops[1].Type == OperandType.Register)
                {
                    if (mandPref != 0) buf.Add(mandPref);
                    int dst = ops[0].RegIndex;
                    int src = ops[1].RegIndex;
                    if (dst >= 8 || src >= 8) buf.Add(MakeRex(0, dst >= 8 ? 1 : 0, 0, src >= 8 ? 1 : 0));
                    buf.Add(0x0F);
                    buf.Add(baseCode);
                    buf.Add(MakeModRm(3, dst & 7, src & 7));
                    return;
                }
                // xmm, [mem]
                else if (ops[0].Type == OperandType.Register && ops[1].Type == OperandType.Memory)
                {
                    int dst = ops[0].RegIndex;
                    EncodeMemAccess(buf, prog, baseCode, dst, ops[1].Mem, false, mandPref, true);
                    return;
                }
                // [mem], xmm (stores)
                else if (ops[0].Type == OperandType.Memory && ops[1].Type == OperandType.Register)
                {
                    int src = ops[1].RegIndex;
                    byte storeCode = baseCode;
                    if (m == "movups") storeCode = 0x11;
                    else if (m == "movaps") storeCode = 0x29;
                    else if (m == "movdqa" || m == "movdqu") storeCode = 0x7F;

                    EncodeMemAccess(buf, prog, storeCode, src, ops[0].Mem, false, mandPref, true);
                    return;
                }
            }

            throw new Exception(string.Format("Unsupported or invalid instruction: '{0}' at line {1}", s.Mnemonic, s.Line));
        }
    }
}
