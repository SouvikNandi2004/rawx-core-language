using System;
using System.Collections.Generic;

namespace RawX
{
    public class Parser
    {
        private List<Token> tokens;
        private int pos = 0;

        public static readonly Dictionary<string, RegInfo> RegMap = new Dictionary<string, RegInfo>(StringComparer.OrdinalIgnoreCase);

        static Parser()
        {
            string[] r64 = { "rax", "rcx", "rdx", "rbx", "rsp", "rbp", "rsi", "rdi", "r8", "r9", "r10", "r11", "r12", "r13", "r14", "r15" };
            for (int i = 0; i < 16; i++) RegMap[r64[i]] = new RegInfo(i, 64);

            string[] r32 = { "eax", "ecx", "edx", "ebx", "esp", "ebp", "esi", "edi", "r8d", "r9d", "r10d", "r11d", "r12d", "r13d", "r14d", "r15d" };
            for (int i = 0; i < 16; i++) RegMap[r32[i]] = new RegInfo(i, 32);

            string[] r16 = { "ax", "cx", "dx", "bx", "sp", "bp", "si", "di", "r8w", "r9w", "r10w", "r11w", "r12w", "r13w", "r14w", "r15w" };
            for (int i = 0; i < 16; i++) RegMap[r16[i]] = new RegInfo(i, 16);

            string[] r8 = { "al", "cl", "dl", "bl", "spl", "bpl", "sil", "dil", "r8b", "r9b", "r10b", "r11b", "r12b", "r13b", "r14b", "r15b" };
            for (int i = 0; i < 16; i++) RegMap[r8[i]] = new RegInfo(i, 8);

            // Legacy high 8-bit registers (no REX prefix allowed)
            RegMap["ah"] = new RegInfo(4, 8);
            RegMap["ch"] = new RegInfo(5, 8);
            RegMap["dh"] = new RegInfo(6, 8);
            RegMap["bh"] = new RegInfo(7, 8);

            // 128-bit XMM SSE/AVX registers
            for (int i = 0; i < 16; i++)
            {
                RegMap["xmm" + i] = new RegInfo(i, 128, true);
            }
        }

        public Parser(List<Token> tokens)
        {
            this.tokens = tokens;
            this.pos = 0;
        }

        public Token Current
        {
            get
            {
                if (pos >= tokens.Count) return new Token(TokenType.EOF, "", 0, 0);
                return tokens[pos];
            }
        }

        public Token Advance()
        {
            Token tok = Current;
            pos++;
            return tok;
        }

        public void SkipNewlines()
        {
            while (Current.Type == TokenType.Newline)
            {
                Advance();
            }
        }

        public static bool IsDataDirective(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string lower = s.ToLowerInvariant();
            return lower == "db" || lower == "dw" || lower == "dd" || lower == "dq" ||
                   lower == "resb" || lower == "resw" || lower == "resd" || lower == "resq";
        }

        public ProgramAST Parse()
        {
            ProgramAST ast = new ProgramAST();

            while (Current.Type != TokenType.EOF)
            {
                SkipNewlines();
                if (Current.Type == TokenType.EOF) break;

                // Handle directives like "section .text" or "entry main" or "align 16"
                if (Current.Type == TokenType.Identifier &&
                    (Current.Value.Equals("section", StringComparison.OrdinalIgnoreCase) ||
                     Current.Value.Equals("entry", StringComparison.OrdinalIgnoreCase) ||
                     Current.Value.Equals("global", StringComparison.OrdinalIgnoreCase) ||
                     Current.Value.Equals("align", StringComparison.OrdinalIgnoreCase) ||
                     Current.Value.Equals("times", StringComparison.OrdinalIgnoreCase)))
                {
                    Statement dirStmt = ParseDirective();
                    ast.Statements.Add(dirStmt);
                    if (dirStmt.Directive.Equals("entry", StringComparison.OrdinalIgnoreCase) && dirStmt.DirectiveArgs.Count > 0)
                    {
                        ast.EntrySymbol = dirStmt.DirectiveArgs[0].ToString();
                    }
                    continue;
                }

                // Check for label or instruction or data definition
                string label = null;
                int line = Current.Line;

                // If identifier followed by colon, or followed by data directive
                if (Current.Type == TokenType.Identifier)
                {
                    if (pos + 1 < tokens.Count && tokens[pos + 1].Type == TokenType.Colon)
                    {
                        label = Advance().Value; // identifier
                        Advance(); // skip ':'
                        SkipNewlines();

                        // Could be just a label on its own line
                        if (Current.Type == TokenType.Newline || Current.Type == TokenType.EOF)
                        {
                            Statement lblStmt = new Statement { Label = label, Line = line };
                            ast.Statements.Add(lblStmt);
                            continue;
                        }
                    }
                    else if (pos + 1 < tokens.Count && tokens[pos + 1].Type == TokenType.Identifier && IsDataDirective(tokens[pos + 1].Value))
                    {
                        label = Advance().Value; // data label
                    }
                }

                // Check if next token is a data directive
                if (Current.Type == TokenType.Identifier && IsDataDirective(Current.Value))
                {
                    Statement dataStmt = new Statement { Label = label, Line = line };
                    ParseDataDefInto(dataStmt);
                    ast.Statements.Add(dataStmt);
                    continue;
                }

                // Otherwise it's an instruction
                Statement instStmt = ParseInstruction();
                if (instStmt != null)
                {
                    if (!string.IsNullOrEmpty(label))
                    {
                        instStmt.Label = label;
                    }
                    ast.Statements.Add(instStmt);
                }
            }

            return ast;
        }

        public Statement ParseDirective()
        {
            Statement stmt = new Statement();
            stmt.Line = Current.Line;
            stmt.Directive = Advance().Value.ToLowerInvariant();

            while (Current.Type != TokenType.Newline && Current.Type != TokenType.EOF)
            {
                if (Current.Type == TokenType.Dot)
                {
                    Advance();
                    if (Current.Type == TokenType.Identifier)
                    {
                        stmt.DirectiveArgs.Add("." + Advance().Value);
                    }
                }
                else if (Current.Type == TokenType.Identifier || Current.Type == TokenType.Register)
                {
                    stmt.DirectiveArgs.Add(Advance().Value);
                }
                else if (Current.Type == TokenType.Number)
                {
                    stmt.DirectiveArgs.Add(Advance().NumValue);
                }
                else if (Current.Type == TokenType.String)
                {
                    stmt.DirectiveArgs.Add(Advance().Value);
                }
                else
                {
                    Advance();
                }

                if (Current.Type == TokenType.Comma)
                {
                    Advance();
                }
            }

            return stmt;
        }

        public void ParseDataDefInto(Statement stmt)
        {
            stmt.Directive = Advance().Value.ToLowerInvariant();

            while (Current.Type != TokenType.Newline && Current.Type != TokenType.EOF)
            {
                if (Current.Type == TokenType.Number)
                {
                    stmt.DirectiveArgs.Add(Advance().NumValue);
                }
                else if (Current.Type == TokenType.String)
                {
                    stmt.DirectiveArgs.Add(Advance().Value);
                }
                else if (Current.Type == TokenType.Identifier)
                {
                    stmt.DirectiveArgs.Add(Advance().Value);
                }
                else
                {
                    Advance();
                }

                if (Current.Type == TokenType.Comma)
                {
                    Advance();
                }
            }
        }

        public Statement ParseInstruction()
        {
            if (Current.Type != TokenType.Identifier && Current.Type != TokenType.Register)
            {
                Advance();
                return null;
            }

            Statement stmt = new Statement();
            stmt.Line = Current.Line;
            stmt.Mnemonic = Advance().Value.ToLowerInvariant();

            // Handle lock prefix e.g. "lock cmpxchg [rax], rbx"
            if (stmt.Mnemonic == "lock" && Current.Type == TokenType.Identifier)
            {
                stmt.Mnemonic = "lock " + Advance().Value.ToLowerInvariant();
            }

            // Parse comma-separated operands
            while (Current.Type != TokenType.Newline && Current.Type != TokenType.EOF)
            {
                Operand op = ParseOperand();
                if (op != null)
                {
                    stmt.Operands.Add(op);
                }

                if (Current.Type == TokenType.Comma)
                {
                    Advance();
                }
                else
                {
                    break;
                }
            }

            return stmt;
        }

        public Operand ParseOperand()
        {
            if (Current.Type == TokenType.Register)
            {
                string rname = Advance().Value;
                RegInfo info = RegMap[rname];
                return new Operand
                {
                    Type = OperandType.Register,
                    Reg = rname,
                    RegSize = info.Size,
                    RegIndex = info.Index,
                    IsXmm = info.IsXmm
                };
            }

            if (Current.Type == TokenType.Number)
            {
                long val = Advance().NumValue;
                return new Operand
                {
                    Type = OperandType.Immediate,
                    Imm = val
                };
            }

            // Size specifier before memory: byte, word, dword, qword, xmmword
            int explicitSize = 64;
            if (Current.Type == TokenType.Identifier &&
                (Current.Value.Equals("byte", StringComparison.OrdinalIgnoreCase) ||
                 Current.Value.Equals("word", StringComparison.OrdinalIgnoreCase) ||
                 Current.Value.Equals("dword", StringComparison.OrdinalIgnoreCase) ||
                 Current.Value.Equals("qword", StringComparison.OrdinalIgnoreCase) ||
                 Current.Value.Equals("xmmword", StringComparison.OrdinalIgnoreCase)))
            {
                string szName = Advance().Value.ToLowerInvariant();
                switch (szName)
                {
                    case "byte": explicitSize = 8; break;
                    case "word": explicitSize = 16; break;
                    case "dword": explicitSize = 32; break;
                    case "qword": explicitSize = 64; break;
                    case "xmmword": explicitSize = 128; break;
                }

                // Skip optional "ptr" e.g. "qword ptr [rax]"
                if (Current.Type == TokenType.Identifier && Current.Value.Equals("ptr", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                }
            }

            if (Current.Type == TokenType.LBracket)
            {
                Advance(); // '['
                MemoryOperand mem = ParseMemoryInside(explicitSize);
                if (Current.Type == TokenType.RBracket)
                {
                    Advance(); // ']'
                }
                return new Operand
                {
                    Type = OperandType.Memory,
                    Mem = mem
                };
            }

            if (Current.Type == TokenType.Identifier)
            {
                string lbl = Advance().Value;
                return new Operand
                {
                    Type = OperandType.Label,
                    Label = lbl
                };
            }

            return null;
        }

        public MemoryOperand ParseMemoryInside(int size)
        {
            MemoryOperand mem = new MemoryOperand { Size = size };

            // Check if RIP-relative: [rip + label] or [label]
            if (Current.Type == TokenType.Register && Current.Value.Equals("rip", StringComparison.OrdinalIgnoreCase))
            {
                Advance(); // 'rip'
                if (Current.Type == TokenType.Plus) Advance();
                if (Current.Type == TokenType.Identifier)
                {
                    mem.Label = Advance().Value;
                }
                if (Current.Type == TokenType.Plus || Current.Type == TokenType.Minus)
                {
                    bool isNeg = Current.Type == TokenType.Minus;
                    Advance();
                    if (Current.Type == TokenType.Number)
                    {
                        long num = Advance().NumValue;
                        mem.Disp = isNeg ? -num : num;
                    }
                }
                return mem;
            }

            // Check if single label [label]
            if (Current.Type == TokenType.Identifier && !RegMap.ContainsKey(Current.Value))
            {
                mem.Label = Advance().Value;
                if (Current.Type == TokenType.Plus || Current.Type == TokenType.Minus)
                {
                    bool isNeg = Current.Type == TokenType.Minus;
                    Advance();
                    if (Current.Type == TokenType.Number)
                    {
                        long num = Advance().NumValue;
                        mem.Disp = isNeg ? -num : num;
                    }
                }
                return mem;
            }

            // Base register
            if (Current.Type == TokenType.Register)
            {
                mem.BaseReg = Advance().Value;
            }

            // Additional terms (+ or -)
            while (Current.Type == TokenType.Plus || Current.Type == TokenType.Minus)
            {
                bool isMinus = Current.Type == TokenType.Minus;
                Advance();

                if (Current.Type == TokenType.Register)
                {
                    string reg = Advance().Value;
                    // Check if multiplied by scale e.g. "rcx * 8"
                    int scale = 1;
                    if (Current.Type == TokenType.Star)
                    {
                        Advance(); // '*'
                        if (Current.Type == TokenType.Number)
                        {
                            scale = (int)Advance().NumValue;
                        }
                    }

                    if (mem.BaseReg == null)
                    {
                        mem.BaseReg = reg;
                    }
                    else
                    {
                        mem.IndexReg = reg;
                        mem.Scale = scale;
                    }
                }
                else if (Current.Type == TokenType.Number)
                {
                    long val = Advance().NumValue;
                    mem.Disp += isMinus ? -val : val;
                }
                else if (Current.Type == TokenType.Identifier)
                {
                    mem.Label = Advance().Value;
                }
            }

            return mem;
        }
    }
}
