using System;
using System.Collections.Generic;
using System.Text;

namespace RawX
{
    public class Lexer
    {
        private string src;
        private int pos;
        private int line = 1;
        private int col = 1;

        public static readonly HashSet<string> Registers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 64-bit GPRs
            "rax", "rcx", "rdx", "rbx", "rsp", "rbp", "rsi", "rdi",
            "r8", "r9", "r10", "r11", "r12", "r13", "r14", "r15",
            // 32-bit GPRs
            "eax", "ecx", "edx", "ebx", "esp", "ebp", "esi", "edi",
            "r8d", "r9d", "r10d", "r11d", "r12d", "r13d", "r14d", "r15d",
            // 16-bit GPRs
            "ax", "cx", "dx", "bx", "sp", "bp", "si", "di",
            "r8w", "r9w", "r10w", "r11w", "r12w", "r13w", "r14w", "r15w",
            // 8-bit GPRs
            "al", "cl", "dl", "bl", "spl", "bpl", "sil", "dil",
            "r8b", "r9b", "r10b", "r11b", "r12b", "r13b", "r14b", "r15b",
            "ah", "ch", "dh", "bh",
            // 128-bit SIMD XMM registers
            "xmm0", "xmm1", "xmm2", "xmm3", "xmm4", "xmm5", "xmm6", "xmm7",
            "xmm8", "xmm9", "xmm10", "xmm11", "xmm12", "xmm13", "xmm14", "xmm15"
        };

        public Lexer(string source)
        {
            src = source ?? string.Empty;
            pos = 0;
            line = 1;
            col = 1;
        }

        private char Peek(int offset = 0)
        {
            int p = pos + offset;
            if (p >= src.Length) return '\0';
            return src[p];
        }

        private char Advance()
        {
            if (pos >= src.Length) return '\0';
            char ch = src[pos++];
            if (ch == '\n')
            {
                line++;
                col = 1;
            }
            else
            {
                col++;
            }
            return ch;
        }

        public List<Token> Tokenize()
        {
            List<Token> tokens = new List<Token>();

            while (pos < src.Length)
            {
                char ch = Peek();

                // Skip spaces and tabs
                if (ch == ' ' || ch == '\t' || ch == '\r')
                {
                    Advance();
                    continue;
                }

                // Comments: // or ;
                if (ch == ';' || (ch == '/' && Peek(1) == '/'))
                {
                    while (pos < src.Length && Peek() != '\n')
                    {
                        Advance();
                    }
                    continue;
                }

                // Block comments /* ... */
                if (ch == '/' && Peek(1) == '*')
                {
                    Advance(); Advance();
                    while (pos < src.Length && !(Peek() == '*' && Peek(1) == '/'))
                    {
                        Advance();
                    }
                    if (pos < src.Length) { Advance(); Advance(); }
                    continue;
                }

                // Newlines
                if (ch == '\n')
                {
                    int startLine = line;
                    int startCol = col;
                    Advance();
                    tokens.Add(new Token(TokenType.Newline, "\n", startLine, startCol));
                    continue;
                }

                int tokLine = line;
                int tokCol = col;

                // Single character punctuation
                if (ch == ',') { Advance(); tokens.Add(new Token(TokenType.Comma, ",", tokLine, tokCol)); continue; }
                if (ch == ':') { Advance(); tokens.Add(new Token(TokenType.Colon, ":", tokLine, tokCol)); continue; }
                if (ch == '[') { Advance(); tokens.Add(new Token(TokenType.LBracket, "[", tokLine, tokCol)); continue; }
                if (ch == ']') { Advance(); tokens.Add(new Token(TokenType.RBracket, "]", tokLine, tokCol)); continue; }
                if (ch == '+') { Advance(); tokens.Add(new Token(TokenType.Plus, "+", tokLine, tokCol)); continue; }
                if (ch == '*') { Advance(); tokens.Add(new Token(TokenType.Star, "*", tokLine, tokCol)); continue; }
                if (ch == '.') { Advance(); tokens.Add(new Token(TokenType.Dot, ".", tokLine, tokCol)); continue; }

                // Minus: could be operator '-' or negative number
                if (ch == '-')
                {
                    Advance();
                    if (char.IsDigit(Peek()))
                    {
                        tokens.Add(ReadNumber(tokLine, tokCol, true));
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Minus, "-", tokLine, tokCol));
                    }
                    continue;
                }

                // Strings: "..." or '...'
                if (ch == '"' || ch == '\'')
                {
                    char quote = Advance();
                    StringBuilder sb = new StringBuilder();
                    while (pos < src.Length && Peek() != quote)
                    {
                        char c = Advance();
                        if (c == '\\' && pos < src.Length)
                        {
                            char esc = Advance();
                            switch (esc)
                            {
                                case 'n': sb.Append('\n'); break;
                                case 'r': sb.Append('\r'); break;
                                case 't': sb.Append('\t'); break;
                                case '0': sb.Append('\0'); break;
                                case '\\': sb.Append('\\'); break;
                                case '"': sb.Append('"'); break;
                                case '\'': sb.Append('\''); break;
                                case 'x':
                                    // 2 hex digits
                                    string hex = "";
                                    if (pos < src.Length && Uri.IsHexDigit(Peek())) hex += Advance();
                                    if (pos < src.Length && Uri.IsHexDigit(Peek())) hex += Advance();
                                    if (hex.Length > 0)
                                        sb.Append((char)Convert.ToInt32(hex, 16));
                                    break;
                                default:
                                    sb.Append(esc);
                                    break;
                            }
                        }
                        else
                        {
                            sb.Append(c);
                        }
                    }
                    if (pos < src.Length && Peek() == quote) Advance();
                    tokens.Add(new Token(TokenType.String, sb.ToString(), tokLine, tokCol));
                    continue;
                }

                // Numbers: digits or 0x / 0b
                if (char.IsDigit(ch))
                {
                    tokens.Add(ReadNumber(tokLine, tokCol, false));
                    continue;
                }

                // Identifiers or Registers or Directives
                if (char.IsLetter(ch) || ch == '_' || ch == '%' || ch == '$' || ch == '@')
                {
                    StringBuilder sb = new StringBuilder();
                    while (pos < src.Length && (char.IsLetterOrDigit(Peek()) || Peek() == '_' || Peek() == '%' || Peek() == '$' || Peek() == '@' || Peek() == '.'))
                    {
                        sb.Append(Advance());
                    }

                    string ident = sb.ToString();
                    if (Registers.Contains(ident))
                    {
                        tokens.Add(new Token(TokenType.Register, ident, tokLine, tokCol));
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Identifier, ident, tokLine, tokCol));
                    }
                    continue;
                }

                // Unknown character
                Advance();
            }

            tokens.Add(new Token(TokenType.EOF, "", line, col));
            return tokens;
        }

        private Token ReadNumber(int line, int col, bool isNegative)
        {
            StringBuilder sb = new StringBuilder();
            if (isNegative) sb.Append('-');

            if (Peek() == '0' && (Peek(1) == 'x' || Peek(1) == 'X'))
            {
                Advance(); Advance(); // skip 0x
                while (pos < src.Length && (char.IsDigit(Peek()) || (Peek() >= 'a' && Peek() <= 'f') || (Peek() >= 'A' && Peek() <= 'F')))
                {
                    sb.Append(Advance());
                }
                long val = Convert.ToInt64(sb.ToString(), 16);
                if (isNegative) val = -val;
                return new Token(TokenType.Number, sb.ToString(), line, col, val);
            }
            else if (Peek() == '0' && (Peek(1) == 'b' || Peek(1) == 'B'))
            {
                Advance(); Advance(); // skip 0b
                while (pos < src.Length && (Peek() == '0' || Peek() == '1'))
                {
                    sb.Append(Advance());
                }
                long val = Convert.ToInt64(sb.ToString(), 2);
                if (isNegative) val = -val;
                return new Token(TokenType.Number, sb.ToString(), line, col, val);
            }
            else
            {
                while (pos < src.Length && char.IsDigit(Peek()))
                {
                    sb.Append(Advance());
                }
                long val;
                if (!long.TryParse(sb.ToString(), out val))
                {
                    val = 0;
                }
                return new Token(TokenType.Number, sb.ToString(), line, col, val);
            }
        }
    }
}
