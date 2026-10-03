using System;
using System.Collections.Generic;

namespace RawX
{
    public enum TokenType
    {
        Identifier,
        Number,
        String,
        Register,
        Comma,
        Colon,
        LBracket,
        RBracket,
        Plus,
        Minus,
        Star,
        Dot,
        Newline,
        EOF
    }

    public class Token
    {
        public TokenType Type;
        public string Value;
        public long NumValue;
        public int Line;
        public int Column;

        public Token(TokenType type, string value, int line, int column, long numValue = 0)
        {
            Type = type;
            Value = value;
            Line = line;
            Column = column;
            NumValue = numValue;
        }

        public override string ToString()
        {
            return string.Format("{0}({1}) at line {2}:{3}", Type, Value, Line, Column);
        }
    }

    public class RegInfo
    {
        public int Index;
        public int Size; // 8, 16, 32, 64, 128
        public bool IsXmm;

        public RegInfo(int index, int size, bool isXmm = false)
        {
            Index = index;
            Size = size;
            IsXmm = isXmm;
        }
    }

    public enum OperandType
    {
        Register,
        Immediate,
        Memory,
        Label
    }

    public class MemoryOperand
    {
        public string BaseReg;
        public string IndexReg;
        public int Scale = 1;
        public long Disp = 0;
        public string Label;
        public int Size = 64; // default 64-bit qword
    }

    public class Operand
    {
        public OperandType Type;
        public string Reg;
        public int RegSize;
        public int RegIndex;
        public bool IsXmm;
        public long Imm;
        public MemoryOperand Mem;
        public string Label;
    }

    public class Statement
    {
        public string Label;
        public string Mnemonic;
        public List<Operand> Operands = new List<Operand>();
        public string Directive;
        public List<object> DirectiveArgs = new List<object>();
        public int Line;
    }

    public class ProgramAST
    {
        public List<Statement> Statements = new List<Statement>();
        public string EntrySymbol = "main";
    }

    public class Relocation
    {
        public int Offset;
        public string TargetSymbol;
        public string Type; // "IMPORT_CALL", "REL32", "RIP_REL32", "ABS64"
        public int Addend;
    }

    public class SymbolLocation
    {
        public string Section;
        public int Offset;

        public SymbolLocation(string section, int offset)
        {
            Section = section;
            Offset = offset;
        }
    }

    public class EncodedProgram
    {
        public List<byte> TextBytes = new List<byte>();
        public List<byte> DataBytes = new List<byte>();
        public int BssSize = 0;
        public Dictionary<string, SymbolLocation> Symbols = new Dictionary<string, SymbolLocation>(StringComparer.OrdinalIgnoreCase);
        public List<Relocation> Relocations = new List<Relocation>();
        public string EntrySymbol = "main";
    }

    public enum TargetPlatform
    {
        Windows,
        Linux,
        MacOS
    }

    public enum BinaryFormat
    {
        PE,
        ELF,
        MachO,
        FlatBin
    }
}
