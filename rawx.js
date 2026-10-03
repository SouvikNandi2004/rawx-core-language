#!/usr/bin/env node
// ============================================================================
// RawX: Bare-Metal Systems Programming Language for Node.js
// 100% Pure JavaScript | Zero Dependencies | Browser & Node.js Universal
// Supports Windows (PE32+), Linux (ELF64), macOS (Mach-O 64), and Bare-Metal
// https://github.com/SouvikNandi2004/rawx-core-language
// ============================================================================

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

// --- 1. Lexer & Tokens ---
const TokenType = {
    Identifier: 'Identifier',
    Number: 'Number',
    String: 'String',
    Register: 'Register',
    Comma: 'Comma',
    Colon: 'Colon',
    LBracket: 'LBracket',
    RBracket: 'RBracket',
    Plus: 'Plus',
    Minus: 'Minus',
    Star: 'Star',
    Dot: 'Dot',
    Newline: 'Newline',
    EOF: 'EOF'
};

const REGISTERS = new Set([
    // 64-bit
    'rax', 'rcx', 'rdx', 'rbx', 'rsp', 'rbp', 'rsi', 'rdi',
    'r8', 'r9', 'r10', 'r11', 'r12', 'r13', 'r14', 'r15',
    // 32-bit
    'eax', 'ecx', 'edx', 'ebx', 'esp', 'ebp', 'esi', 'edi',
    'r8d', 'r9d', 'r10d', 'r11d', 'r12d', 'r13d', 'r14d', 'r15d',
    // 16-bit
    'ax', 'cx', 'dx', 'bx', 'sp', 'bp', 'si', 'di',
    'r8w', 'r9w', 'r10w', 'r11w', 'r12w', 'r13w', 'r14w', 'r15w',
    // 8-bit
    'al', 'cl', 'dl', 'bl', 'spl', 'bpl', 'sil', 'dil',
    'r8b', 'r9b', 'r10b', 'r11b', 'r12b', 'r13b', 'r14b', 'r15b',
    'ah', 'ch', 'dh', 'bh',
    // SIMD XMM
    'xmm0', 'xmm1', 'xmm2', 'xmm3', 'xmm4', 'xmm5', 'xmm6', 'xmm7',
    'xmm8', 'xmm9', 'xmm10', 'xmm11', 'xmm12', 'xmm13', 'xmm14', 'xmm15'
]);

class Lexer {
    constructor(src) {
        this.src = src || '';
        this.pos = 0;
        this.line = 1;
        this.col = 1;
    }

    peek(offset = 0) {
        const p = this.pos + offset;
        return p >= this.src.length ? '\0' : this.src[p];
    }

    advance() {
        if (this.pos >= this.src.length) return '\0';
        const ch = this.src[this.pos++];
        if (ch === '\n') {
            this.line++;
            this.col = 1;
        } else {
            this.col++;
        }
        return ch;
    }

    tokenize() {
        const tokens = [];
        while (this.pos < this.src.length) {
            const ch = this.peek();

            if (ch === ' ' || ch === '\t' || ch === '\r') {
                this.advance();
                continue;
            }

            if (ch === ';' || (ch === '/' && this.peek(1) === '/')) {
                while (this.pos < this.src.length && this.peek() !== '\n') {
                    this.advance();
                }
                continue;
            }

            if (ch === '/' && this.peek(1) === '*') {
                this.advance(); this.advance();
                while (this.pos < this.src.length && !(this.peek() === '*' && this.peek(1) === '/')) {
                    this.advance();
                }
                if (this.pos < this.src.length) { this.advance(); this.advance(); }
                continue;
            }

            if (ch === '\n') {
                tokens.push({ type: TokenType.Newline, value: '\n', line: this.line, col: this.col });
                this.advance();
                continue;
            }

            const tokLine = this.line;
            const tokCol = this.col;

            if (ch === ',') { this.advance(); tokens.push({ type: TokenType.Comma, value: ',', line: tokLine, col: tokCol }); continue; }
            if (ch === ':') { this.advance(); tokens.push({ type: TokenType.Colon, value: ':', line: tokLine, col: tokCol }); continue; }
            if (ch === '[') { this.advance(); tokens.push({ type: TokenType.LBracket, value: '[', line: tokLine, col: tokCol }); continue; }
            if (ch === ']') { this.advance(); tokens.push({ type: TokenType.RBracket, value: ']', line: tokLine, col: tokCol }); continue; }
            if (ch === '+') { this.advance(); tokens.push({ type: TokenType.Plus, value: '+', line: tokLine, col: tokCol }); continue; }
            if (ch === '*') { this.advance(); tokens.push({ type: TokenType.Star, value: '*', line: tokLine, col: tokCol }); continue; }
            if (ch === '.') { this.advance(); tokens.push({ type: TokenType.Dot, value: '.', line: tokLine, col: tokCol }); continue; }

            if (ch === '-') {
                this.advance();
                if (/[0-9]/.test(this.peek())) {
                    tokens.push(this.readNumber(tokLine, tokCol, true));
                } else {
                    tokens.push({ type: TokenType.Minus, value: '-', line: tokLine, col: tokCol });
                }
                continue;
            }

            if (ch === '"' || ch === "'") {
                const quote = this.advance();
                let str = '';
                while (this.pos < this.src.length && this.peek() !== quote) {
                    const c = this.advance();
                    if (c === '\\' && this.pos < this.src.length) {
                        const esc = this.advance();
                        if (esc === 'n') str += '\n';
                        else if (esc === 'r') str += '\r';
                        else if (esc === 't') str += '\t';
                        else if (esc === '0') str += '\0';
                        else if (esc === '\\') str += '\\';
                        else if (esc === '"') str += '"';
                        else if (esc === "'") str += "'";
                        else str += esc;
                    } else {
                        str += c;
                    }
                }
                if (this.pos < this.src.length && this.peek() === quote) this.advance();
                tokens.push({ type: TokenType.String, value: str, line: tokLine, col: tokCol });
                continue;
            }

            if (/[0-9]/.test(ch)) {
                tokens.push(this.readNumber(tokLine, tokCol, false));
                continue;
            }

            if (/[a-zA-Z_%$@]/.test(ch)) {
                let id = '';
                while (this.pos < this.src.length && /[a-zA-Z0-9_%$@.]/.test(this.peek())) {
                    id += this.advance();
                }
                const lower = id.toLowerCase();
                if (REGISTERS.has(lower)) {
                    tokens.push({ type: TokenType.Register, value: lower, line: tokLine, col: tokCol });
                } else {
                    tokens.push({ type: TokenType.Identifier, value: id, line: tokLine, col: tokCol });
                }
                continue;
            }

            this.advance();
        }

        tokens.push({ type: TokenType.EOF, value: '', line: this.line, col: this.col });
        return tokens;
    }

    readNumber(line, col, isNeg) {
        let numStr = isNeg ? '-' : '';
        if (this.peek() === '0' && (this.peek(1) === 'x' || this.peek(1) === 'X')) {
            this.advance(); this.advance();
            let hex = '';
            while (this.pos < this.src.length && /[0-9a-fA-F]/.test(this.peek())) {
                hex += this.advance();
            }
            const val = BigInt('0x' + (hex || '0')) * (isNeg ? -1n : 1n);
            return { type: TokenType.Number, value: hex, numValue: val, line, col };
        } else if (this.peek() === '0' && (this.peek(1) === 'b' || this.peek(1) === 'B')) {
            this.advance(); this.advance();
            let bin = '';
            while (this.pos < this.src.length && /[01]/.test(this.peek())) {
                bin += this.advance();
            }
            const val = BigInt('0b' + (bin || '0')) * (isNeg ? -1n : 1n);
            return { type: TokenType.Number, value: bin, numValue: val, line, col };
        } else {
            let digits = '';
            while (this.pos < this.src.length && /[0-9]/.test(this.peek())) {
                digits += this.advance();
            }
            const val = BigInt(numStr + digits);
            return { type: TokenType.Number, value: digits, numValue: val, line, col };
        }
    }
}

// --- 2. Register Table ---
const REG_MAP = {};
const r64 = ["rax", "rcx", "rdx", "rbx", "rsp", "rbp", "rsi", "rdi", "r8", "r9", "r10", "r11", "r12", "r13", "r14", "r15"];
r64.forEach((r, i) => REG_MAP[r] = { index: i, size: 64, isXmm: false });
const r32 = ["eax", "ecx", "edx", "ebx", "esp", "ebp", "esi", "edi", "r8d", "r9d", "r10d", "r11d", "r12d", "r13d", "r14d", "r15d"];
r32.forEach((r, i) => REG_MAP[r] = { index: i, size: 32, isXmm: false });
const r16 = ["ax", "cx", "dx", "bx", "sp", "bp", "si", "di", "r8w", "r9w", "r10w", "r11w", "r12w", "r13w", "r14w", "r15w"];
r16.forEach((r, i) => REG_MAP[r] = { index: i, size: 16, isXmm: false });
const r8 = ["al", "cl", "dl", "bl", "spl", "bpl", "sil", "dil", "r8b", "r9b", "r10b", "r11b", "r12b", "r13b", "r14b", "r15b"];
r8.forEach((r, i) => REG_MAP[r] = { index: i, size: 8, isXmm: false });
REG_MAP["ah"] = { index: 4, size: 8, isXmm: false };
REG_MAP["ch"] = { index: 5, size: 8, isXmm: false };
REG_MAP["dh"] = { index: 6, size: 8, isXmm: false };
REG_MAP["bh"] = { index: 7, size: 8, isXmm: false };
for (let i = 0; i < 16; i++) {
    REG_MAP["xmm" + i] = { index: i, size: 128, isXmm: true };
}

// --- 3. Preprocessor ---
class Preprocessor {
    constructor(target = 'windows', rootDir = process.cwd()) {
        this.target = target.toLowerCase();
        this.rootDir = rootDir;
        this.defined = new Set(['RAWX', 'ARCH_AMD64', 'RAWX_VERSION_2']);
        this.defines = new Map();
        this.included = new Set();

        if (this.target === 'windows' || this.target === 'win') {
            ['TARGET_WINDOWS', '__WINDOWS__', '_WIN64', 'OS_WINDOWS'].forEach(s => this.defined.add(s));
        } else if (this.target === 'linux') {
            ['TARGET_LINUX', '__LINUX__', '_LINUX64', 'OS_LINUX'].forEach(s => this.defined.add(s));
        } else if (this.target === 'macos' || this.target === 'darwin') {
            ['TARGET_MACOS', '__MACOS__', '__DARWIN__', '_MACOS64', 'OS_MACOS'].forEach(s => this.defined.add(s));
        }
    }

    async process(source, currentFile = null) {
        if (currentFile) this.included.add(path.resolve(currentFile));
        const lines = source.split(/\r?\n/);
        const condStack = [true];
        const out = [];

        for (let idx = 0; idx < lines.length; idx++) {
            let line = lines[idx].trim();

            if (/^%ifdef\s+/i.test(line)) {
                const sym = line.replace(/^%ifdef\s+/i, '').trim();
                const parent = condStack[condStack.length - 1];
                condStack.push(parent && this.defined.has(sym));
                out.push('');
                continue;
            }
            if (/^%ifndef\s+/i.test(line)) {
                const sym = line.replace(/^%ifndef\s+/i, '').trim();
                const parent = condStack[condStack.length - 1];
                condStack.push(parent && !this.defined.has(sym));
                out.push('');
                continue;
            }
            if (/^%else\b/i.test(line)) {
                if (condStack.length > 1) {
                    const cur = condStack.pop();
                    const parent = condStack[condStack.length - 1];
                    condStack.push(parent && !cur);
                }
                out.push('');
                continue;
            }
            if (/^%endif\b/i.test(line)) {
                if (condStack.length > 1) condStack.pop();
                out.push('');
                continue;
            }

            if (!condStack[condStack.length - 1]) {
                out.push('');
                continue;
            }

            if (/^%define\s+/i.test(line)) {
                const rest = line.replace(/^%define\s+/i, '').trim();
                const sp = rest.indexOf(' ');
                if (sp > 0) {
                    const k = rest.slice(0, sp).trim();
                    const v = rest.slice(sp + 1).trim();
                    this.defined.add(k);
                    this.defines.set(k, v);
                } else if (rest) {
                    this.defined.add(rest);
                }
                out.push('');
                continue;
            }

            const equMatch = line.match(/^([a-zA-Z_0-9]+):?\s+equ\s+(.+)$/i);
            if (equMatch && !line.startsWith('//') && !line.startsWith(';')) {
                this.defined.add(equMatch[1]);
                this.defines.set(equMatch[1], equMatch[2].trim());
                out.push('');
                continue;
            }

            if (/^(%include|include)\s+"([^"]+)"/i.test(line)) {
                const m = line.match(/^(?:%include|include)\s+"([^"]+)"/i);
                const inc = m[1];

                if (/^https?:\/\//i.test(inc)) {
                    // Fetch remote include URL (e.g. GitHub raw link)
                    if (!this.included.has(inc)) {
                        this.included.add(inc);
                        let text = '';
                        if (typeof fetch !== 'undefined') {
                            const resp = await fetch(inc);
                            if (!resp.ok) throw new Error(`HTTP ${resp.status} fetching ${inc}`);
                            text = await resp.text();
                        } else {
                            throw new Error(`fetch is required for remote include: ${inc}`);
                        }
                        const sub = await this.process(text, null);
                        out.push(sub);
                    }
                    continue;
                }

                const base = currentFile ? path.dirname(currentFile) : this.rootDir;
                let incPath = path.resolve(base, inc);
                if (!fs.existsSync(incPath)) incPath = path.resolve(this.rootDir, inc);

                if (fs.existsSync(incPath)) {
                    const full = path.resolve(incPath);
                    if (!this.included.has(full)) {
                        this.included.add(full);
                        const text = fs.readFileSync(full, 'utf8');
                        const sub = await this.process(text, full);
                        out.push(sub);
                    }
                } else {
                    throw new Error(`Include file not found: '${inc}'`);
                }
                continue;
            }

            let proc = lines[idx];
            for (const [k, v] of this.defines.entries()) {
                if (proc.includes(k)) proc = proc.split(k).join(v);
            }
            out.push(proc);
        }

        return out.join('\n');
    }
}

// --- 4. Parser ---
class Parser {
    constructor(tokens) {
        this.tokens = tokens;
        this.pos = 0;
    }

    current() {
        return this.pos >= this.tokens.length ? { type: TokenType.EOF, value: '' } : this.tokens[this.pos];
    }

    advance() {
        const t = this.current();
        this.pos++;
        return t;
    }

    skipNewlines() {
        while (this.current().type === TokenType.Newline) this.advance();
    }

    static isDataDirective(s) {
        if (!s) return false;
        const l = s.toLowerCase();
        return ['db', 'dw', 'dd', 'dq', 'resb', 'resw', 'resd', 'resq'].includes(l);
    }

    parse() {
        const ast = { statements: [], entrySymbol: 'main' };

        while (this.current().type !== TokenType.EOF) {
            this.skipNewlines();
            if (this.current().type === TokenType.EOF) break;

            const cur = this.current();
            if (cur.type === TokenType.Identifier &&
                ['section', 'entry', 'global', 'align', 'times'].includes(cur.value.toLowerCase())) {
                const dirStmt = this.parseDirective();
                ast.statements.push(dirStmt);
                if (dirStmt.directive === 'entry' && dirStmt.args.length > 0) {
                    ast.entrySymbol = dirStmt.args[0];
                }
                continue;
            }

            let label = null;
            const line = cur.line;

            if (cur.type === TokenType.Identifier) {
                if (this.pos + 1 < this.tokens.length && this.tokens[this.pos + 1].type === TokenType.Colon) {
                    label = this.advance().value;
                    this.advance(); // ':'
                    this.skipNewlines();
                    if (this.current().type === TokenType.Newline || this.current().type === TokenType.EOF) {
                        ast.statements.push({ label, line });
                        continue;
                    }
                } else if (this.pos + 1 < this.tokens.length && Parser.isDataDirective(this.tokens[this.pos + 1].value)) {
                    label = this.advance().value;
                }
            }

            if (this.current().type === TokenType.Identifier && Parser.isDataDirective(this.current().value)) {
                const dataStmt = { label, line };
                this.parseDataDefInto(dataStmt);
                ast.statements.push(dataStmt);
                continue;
            }

            const instStmt = this.parseInstruction();
            if (instStmt) {
                if (label) instStmt.label = label;
                ast.statements.push(instStmt);
            }
        }

        return ast;
    }

    parseDirective() {
        const stmt = { line: this.current().line, directive: this.advance().value.toLowerCase(), args: [] };
        while (this.current().type !== TokenType.Newline && this.current().type !== TokenType.EOF) {
            if (this.current().type === TokenType.Dot) {
                this.advance();
                if (this.current().type === TokenType.Identifier) {
                    stmt.args.push('.' + this.advance().value);
                }
            } else if (this.current().type === TokenType.Identifier || this.current().type === TokenType.Register || this.current().type === TokenType.String) {
                stmt.args.push(this.advance().value);
            } else if (this.current().type === TokenType.Number) {
                stmt.args.push(this.advance().numValue);
            } else {
                this.advance();
            }
            if (this.current().type === TokenType.Comma) this.advance();
        }
        return stmt;
    }

    parseDataDefInto(stmt) {
        stmt.directive = this.advance().value.toLowerCase();
        stmt.args = [];
        while (this.current().type !== TokenType.Newline && this.current().type !== TokenType.EOF) {
            if (this.current().type === TokenType.Number) stmt.args.push(this.advance().numValue);
            else if (this.current().type === TokenType.String) stmt.args.push(this.advance().value);
            else if (this.current().type === TokenType.Identifier) stmt.args.push(this.advance().value);
            else this.advance();
            if (this.current().type === TokenType.Comma) this.advance();
        }
    }

    parseInstruction() {
        if (this.current().type !== TokenType.Identifier && this.current().type !== TokenType.Register) {
            this.advance();
            return null;
        }

        const stmt = { line: this.current().line, mnemonic: this.advance().value.toLowerCase(), operands: [] };
        if (stmt.mnemonic === 'lock' && this.current().type === TokenType.Identifier) {
            stmt.mnemonic = 'lock ' + this.advance().value.toLowerCase();
        }

        while (this.current().type !== TokenType.Newline && this.current().type !== TokenType.EOF) {
            const op = this.parseOperand();
            if (op) stmt.operands.push(op);
            if (this.current().type === TokenType.Comma) this.advance();
            else break;
        }

        return stmt;
    }

    parseOperand() {
        const cur = this.current();
        if (cur.type === TokenType.Register) {
            const rname = this.advance().value.toLowerCase();
            const info = REG_MAP[rname];
            return { type: 'Register', reg: rname, regSize: info.size, regIndex: info.index, isXmm: info.isXmm };
        }

        if (cur.type === TokenType.Number) {
            return { type: 'Immediate', imm: this.advance().numValue };
        }

        let explicitSize = 64;
        if (cur.type === TokenType.Identifier && ['byte', 'word', 'dword', 'qword', 'xmmword'].includes(cur.value.toLowerCase())) {
            const sz = this.advance().value.toLowerCase();
            if (sz === 'byte') explicitSize = 8;
            else if (sz === 'word') explicitSize = 16;
            else if (sz === 'dword') explicitSize = 32;
            else if (sz === 'qword') explicitSize = 64;
            else if (sz === 'xmmword') explicitSize = 128;
            if (this.current().type === TokenType.Identifier && this.current().value.toLowerCase() === 'ptr') {
                this.advance();
            }
        }

        if (this.current().type === TokenType.LBracket) {
            this.advance(); // '['
            const mem = this.parseMemoryInside(explicitSize);
            if (this.current().type === TokenType.RBracket) this.advance();
            return { type: 'Memory', mem };
        }

        if (this.current().type === TokenType.Identifier) {
            return { type: 'Label', label: this.advance().value };
        }

        return null;
    }

    parseMemoryInside(size) {
        const mem = { size, scale: 1, disp: 0n, baseReg: null, indexReg: null, label: null };

        // Support [rel msg] or [rel + msg]
        if (this.current().type === TokenType.Identifier && this.current().value.toLowerCase() === 'rel') {
            this.advance(); // 'rel'
            if (this.current().type === TokenType.Plus) this.advance();
            if (this.current().type === TokenType.Identifier) mem.label = this.advance().value;
            if (this.current().type === TokenType.Plus || this.current().type === TokenType.Minus) {
                const neg = this.current().type === TokenType.Minus;
                this.advance();
                if (this.current().type === TokenType.Number) {
                    const n = this.advance().numValue;
                    mem.disp = neg ? -n : n;
                }
            }
            return mem;
        }

        if (this.current().type === TokenType.Register && this.current().value.toLowerCase() === 'rip') {
            this.advance(); // 'rip'
            if (this.current().type === TokenType.Plus) this.advance();
            if (this.current().type === TokenType.Identifier) mem.label = this.advance().value;
            if (this.current().type === TokenType.Plus || this.current().type === TokenType.Minus) {
                const neg = this.current().type === TokenType.Minus;
                this.advance();
                if (this.current().type === TokenType.Number) {
                    const n = this.advance().numValue;
                    mem.disp = neg ? -n : n;
                }
            }
            return mem;
        }

        if (this.current().type === TokenType.Identifier && !REG_MAP[this.current().value.toLowerCase()]) {
            mem.label = this.advance().value;
            if (this.current().type === TokenType.Plus || this.current().type === TokenType.Minus) {
                const neg = this.current().type === TokenType.Minus;
                this.advance();
                if (this.current().type === TokenType.Number) {
                    const n = this.advance().numValue;
                    mem.disp = neg ? -n : n;
                }
            }
            return mem;
        }

        if (this.current().type === TokenType.Register) {
            mem.baseReg = this.advance().value.toLowerCase();
        }

        while (this.current().type === TokenType.Plus || this.current().type === TokenType.Minus) {
            const isMinus = this.current().type === TokenType.Minus;
            this.advance();
            if (this.current().type === TokenType.Register) {
                const reg = this.advance().value.toLowerCase();
                let scale = 1;
                if (this.current().type === TokenType.Star) {
                    this.advance();
                    if (this.current().type === TokenType.Number) scale = Number(this.advance().numValue);
                }
                if (!mem.baseReg) mem.baseReg = reg;
                else { mem.indexReg = reg; mem.scale = scale; }
            } else if (this.current().type === TokenType.Number) {
                const v = this.advance().numValue;
                mem.disp += isMinus ? -v : v;
            } else if (this.current().type === TokenType.Identifier) {
                mem.label = this.advance().value;
            }
        }

        return mem;
    }
}

// --- 5. Encoder ---
class Encoder {
    constructor(target = 'windows') {
        this.target = target.toLowerCase();
    }

    makeRex(w, r, x, b) {
        return 0x40 | ((w & 1) << 3) | ((r & 1) << 2) | ((x & 1) << 1) | (b & 1);
    }

    makeModRm(mod, reg, rm) {
        return ((mod & 3) << 6) | ((reg & 7) << 3) | (rm & 7);
    }

    makeSib(scale, index, baseReg) {
        let sc = 0;
        if (scale === 2) sc = 1;
        else if (scale === 4) sc = 2;
        else if (scale === 8) sc = 3;
        return ((sc & 3) << 6) | ((index & 7) << 3) | (baseReg & 7);
    }

    encode(ast) {
        const prog = {
            textBytes: [],
            dataBytes: [],
            bssSize: 0,
            symbols: {},
            relocations: [],
            entrySymbol: ast.entrySymbol || 'main'
        };

        let currentSection = '.text';

        for (const stmt of ast.statements) {
            if (stmt.directive) {
                const dir = stmt.directive;
                if (dir === 'section') {
                    if (stmt.args[0]) currentSection = stmt.args[0].toLowerCase();
                } else if (dir === 'entry' || dir === 'global') {
                    if (stmt.args[0]) prog.entrySymbol = stmt.args[0];
                } else if (dir === 'align') {
                    const align = Number(stmt.args[0] || 1);
                    if (align > 1) {
                        const buf = (currentSection === '.text') ? prog.textBytes : prog.dataBytes;
                        while ((buf.length % align) !== 0) {
                            buf.push(currentSection === '.text' ? 0x90 : 0x00);
                        }
                    }
                } else if (['db', 'dw', 'dd', 'dq'].includes(dir)) {
                    if (stmt.label) prog.symbols[stmt.label] = { section: '.data', offset: prog.dataBytes.length };
                    for (const a of stmt.args) {
                        if (typeof a === 'string') {
                            const b = Buffer.from(a, 'ascii');
                            for (let i = 0; i < b.length; i++) prog.dataBytes.push(b[i]);
                        } else {
                            const val = BigInt(a);
                            if (dir === 'db') prog.dataBytes.push(Number(val & 0xFFn));
                            else if (dir === 'dw') {
                                const b = Buffer.alloc(2); b.writeInt16LE(Number(val));
                                for (let i = 0; i < 2; i++) prog.dataBytes.push(b[i]);
                            } else if (dir === 'dd') {
                                const b = Buffer.alloc(4); b.writeInt32LE(Number(val));
                                for (let i = 0; i < 4; i++) prog.dataBytes.push(b[i]);
                            } else if (dir === 'dq') {
                                const b = Buffer.alloc(8); b.writeBigInt64LE(val);
                                for (let i = 0; i < 8; i++) prog.dataBytes.push(b[i]);
                            }
                        }
                    }
                } else if (['resb', 'resw', 'resd', 'resq'].includes(dir)) {
                    const cnt = Number(stmt.args[0] || 1);
                    const unit = dir === 'resw' ? 2 : dir === 'resd' ? 4 : dir === 'resq' ? 8 : 1;
                    if (stmt.label) prog.symbols[stmt.label] = { section: '.bss', offset: prog.bssSize };
                    prog.bssSize += cnt * unit;
                }
                continue;
            }

            if (stmt.label && !stmt.mnemonic) {
                const off = (currentSection === '.text') ? prog.textBytes.length : prog.dataBytes.length;
                prog.symbols[stmt.label] = { section: currentSection, offset: off };
                continue;
            }

            if (stmt.mnemonic) {
                if (stmt.label) prog.symbols[stmt.label] = { section: '.text', offset: prog.textBytes.length };
                this.encodeInstruction(stmt, prog);
            }
        }

        return prog;
    }

    encodeMemAccess(buf, prog, opcode, regIdx, mem, is64 = true, mandatoryPrefix = 0, has0F = false) {
        if (mandatoryPrefix) buf.push(mandatoryPrefix);

        if (mem.label && !mem.baseReg && !mem.indexReg) {
            const r = (regIdx >= 8) ? 1 : 0;
            const w = is64 ? 1 : 0;
            if (w || r) buf.push(this.makeRex(w, r, 0, 0));
            if (has0F) buf.push(0x0F);
            buf.push(opcode);
            buf.push(this.makeModRm(0, regIdx & 7, 5));
            prog.relocations.push({
                offset: buf.length,
                targetSymbol: mem.label,
                type: 'RIP_REL32',
                addend: Number(mem.disp)
            });
            for (let i = 0; i < 4; i++) buf.push(0);
            return;
        }

        const baseIdx = mem.baseReg ? REG_MAP[mem.baseReg].index : -1;
        const indexIdx = mem.indexReg ? REG_MAP[mem.indexReg].index : -1;

        const rexW = is64 ? 1 : 0;
        const rexR = (regIdx >= 8) ? 1 : 0;
        const rexX = (indexIdx >= 8) ? 1 : 0;
        const rexB = (baseIdx >= 8) ? 1 : 0;

        if (rexW || rexR || rexX || rexB) buf.push(this.makeRex(rexW, rexR, rexX, rexB));
        if (has0F) buf.push(0x0F);
        buf.push(opcode);

        let mod = 0;
        const disp = Number(mem.disp);
        if (disp !== 0 || (baseIdx & 7) === 5) {
            mod = (disp >= -128 && disp <= 127) ? 1 : 2;
        }

        const needsSib = (indexIdx !== -1) || ((baseIdx & 7) === 4);
        if (needsSib) {
            buf.push(this.makeModRm(mod, regIdx & 7, 4));
            const sIdx = (indexIdx !== -1) ? (indexIdx & 7) : 4;
            const sBase = (baseIdx !== -1) ? (baseIdx & 7) : 5;
            buf.push(this.makeSib(mem.scale, sIdx, sBase));
        } else {
            buf.push(this.makeModRm(mod, regIdx & 7, baseIdx & 7));
        }

        if (mod === 1) {
            buf.push(disp & 0xFF);
        } else if (mod === 2 || (disp === 0 && (baseIdx & 7) === 5)) {
            const b = Buffer.alloc(4); b.writeInt32LE(disp);
            for (let i = 0; i < 4; i++) buf.push(b[i]);
        }
    }

    encodeInstruction(s, prog) {
        let m = s.mnemonic.toLowerCase();
        const ops = s.operands;
        const buf = prog.textBytes;

        if (m.startsWith('lock ')) {
            buf.push(0xF0);
            m = m.slice(5).trim();
        }

        if (m === 'nop') { buf.push(0x90); return; }
        if (m === 'ret') { buf.push(0xC3); return; }
        if (m === 'syscall') { buf.push(0x0F, 0x05); return; }
        if (m === 'rdtsc') { buf.push(0x0F, 0x31); return; }
        if (m === 'rdtscp') { buf.push(0x0F, 0x01, 0xF9); return; }
        if (m === 'cpuid') { buf.push(0x0F, 0xA2); return; }
        if (m === 'pause') { buf.push(0xF3, 0x90); return; }
        if (m === 'int3') { buf.push(0xCC); return; }
        if (m === 'leave') { buf.push(0xC9); return; }
        if (m === 'cqo') { buf.push(0x48, 0x99); return; }
        if (m === 'cdq') { buf.push(0x99); return; }

        if (m === 'push' && ops.length === 1) {
            if (ops[0].type === 'Register') {
                const idx = ops[0].regIndex;
                if (idx >= 8) buf.push(this.makeRex(0, 0, 0, 1));
                buf.push(0x50 + (idx & 7));
                return;
            } else if (ops[0].type === 'Immediate') {
                const n = Number(ops[0].imm);
                if (n >= -128 && n <= 127) { buf.push(0x6A, n & 0xFF); }
                else {
                    buf.push(0x68);
                    const b = Buffer.alloc(4); b.writeInt32LE(n);
                    for (let i = 0; i < 4; i++) buf.push(b[i]);
                }
                return;
            }
        }

        if (m === 'pop' && ops.length === 1 && ops[0].type === 'Register') {
            const idx = ops[0].regIndex;
            if (idx >= 8) buf.push(this.makeRex(0, 0, 0, 1));
            buf.push(0x58 + (idx & 7));
            return;
        }

        const branches = {
            jmp: [0xE9],
            je: [0x0F, 0x84], jz: [0x0F, 0x84],
            jne: [0x0F, 0x85], jnz: [0x0F, 0x85],
            jl: [0x0F, 0x8C], jnge: [0x0F, 0x8C],
            jle: [0x0F, 0x8E], jng: [0x0F, 0x8E],
            jg: [0x0F, 0x8F], jnle: [0x0F, 0x8F],
            jge: [0x0F, 0x8D], jnl: [0x0F, 0x8D],
            jb: [0x0F, 0x82], jnae: [0x0F, 0x82], jc: [0x0F, 0x82],
            jbe: [0x0F, 0x86], jna: [0x0F, 0x86],
            ja: [0x0F, 0x87], jnbe: [0x0F, 0x87],
            jae: [0x0F, 0x83], jnb: [0x0F, 0x83], jnc: [0x0F, 0x83],
            call: [0xE8]
        };

        if (branches[m] && ops.length === 1 && ops[0].type === 'Label') {
            const targetSym = ops[0].label;
            const isWin = this.target === 'windows' || this.target === 'win';
            const knownK32 = ['ExitProcess', 'GetStdHandle', 'WriteFile', 'WriteConsoleA', 'ReadFile', 'CreateFileA', 'CloseHandle', 'GetCommandLineA', 'Sleep', 'GetTickCount64', 'VirtualAlloc', 'VirtualFree', 'HeapAlloc', 'HeapFree', 'GetProcessHeap'];
            if (m === 'call' && isWin && (knownK32.includes(targetSym) || targetSym.startsWith('Win'))) {
                buf.push(0xFF, 0x15);
                prog.relocations.push({ offset: buf.length, targetSymbol: targetSym, type: 'IMPORT_CALL' });
                for (let i = 0; i < 4; i++) buf.push(0);
                return;
            }

            buf.push(...branches[m]);
            prog.relocations.push({ offset: buf.length, targetSymbol: targetSym, type: 'REL32' });
            for (let i = 0; i < 4; i++) buf.push(0);
            return;
        }

        if (m === 'call' && ops.length === 1 && ops[0].type === 'Register') {
            const idx = ops[0].regIndex;
            if (idx >= 8) buf.push(this.makeRex(0, 0, 0, 1));
            buf.push(0xFF, this.makeModRm(3, 2, idx & 7));
            return;
        }

        if (m === 'lea' && ops.length === 2 && ops[0].type === 'Register' && ops[1].type === 'Memory') {
            this.encodeMemAccess(buf, prog, 0x8D, ops[0].regIndex, ops[1].mem, true);
            return;
        }

        if (m === 'mov' && ops.length === 2) {
            const op1 = ops[0];
            const op2 = ops[1];

            if (op1.type === 'Register' && op2.type === 'Register') {
                const dst = op1.regIndex;
                const src = op2.regIndex;
                const sz = op1.regSize;
                if (sz === 64) {
                    buf.push(this.makeRex(1, src >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0), 0x89, this.makeModRm(3, src & 7, dst & 7));
                } else if (sz === 32) {
                    if (src >= 8 || dst >= 8) buf.push(this.makeRex(0, src >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0));
                    buf.push(0x89, this.makeModRm(3, src & 7, dst & 7));
                } else if (sz === 16) {
                    buf.push(0x66);
                    if (src >= 8 || dst >= 8) buf.push(this.makeRex(0, src >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0));
                    buf.push(0x89, this.makeModRm(3, src & 7, dst & 7));
                } else if (sz === 8) {
                    if (src >= 4 || dst >= 4) buf.push(this.makeRex(0, src >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0));
                    buf.push(0x88, this.makeModRm(3, src & 7, dst & 7));
                }
                return;
            }

            if (op1.type === 'Register' && op2.type === 'Immediate') {
                const r = op1.regIndex;
                const sz = op1.regSize;
                const imm = BigInt(op2.imm);
                if (sz === 64) {
                    if (imm >= 0n && imm <= 0xFFFFFFFFn) {
                        if (r >= 8) buf.push(this.makeRex(0, 0, 0, 1));
                        buf.push(0xB8 + (r & 7));
                        const b = Buffer.alloc(4); b.writeUInt32LE(Number(imm));
                        for (let i = 0; i < 4; i++) buf.push(b[i]);
                    } else if (imm >= -2147483648n && imm <= 2147483647n) {
                        buf.push(this.makeRex(1, 0, 0, r >= 8 ? 1 : 0), 0xC7, this.makeModRm(3, 0, r & 7));
                        const b = Buffer.alloc(4); b.writeInt32LE(Number(imm));
                        for (let i = 0; i < 4; i++) buf.push(b[i]);
                    } else {
                        buf.push(this.makeRex(1, 0, 0, r >= 8 ? 1 : 0), 0xB8 + (r & 7));
                        const b = Buffer.alloc(8); b.writeBigInt64LE(imm);
                        for (let i = 0; i < 8; i++) buf.push(b[i]);
                    }
                } else if (sz === 32) {
                    if (r >= 8) buf.push(this.makeRex(0, 0, 0, 1));
                    buf.push(0xB8 + (r & 7));
                    const b = Buffer.alloc(4); b.writeInt32LE(Number(imm));
                    for (let i = 0; i < 4; i++) buf.push(b[i]);
                } else if (sz === 16) {
                    buf.push(0x66);
                    if (r >= 8) buf.push(this.makeRex(0, 0, 0, 1));
                    buf.push(0xB8 + (r & 7));
                    const b = Buffer.alloc(2); b.writeInt16LE(Number(imm));
                    for (let i = 0; i < 2; i++) buf.push(b[i]);
                } else if (sz === 8) {
                    if (r >= 4) buf.push(this.makeRex(0, 0, 0, r >= 8 ? 1 : 0));
                    buf.push(0xB0 + (r & 7), Number(imm & 0xFFn));
                }
                return;
            }

            if (op1.type === 'Register' && op2.type === 'Memory') {
                const opc = op1.regSize === 8 ? 0x8A : 0x8B;
                const pref = op1.regSize === 16 ? 0x66 : 0;
                this.encodeMemAccess(buf, prog, opc, op1.regIndex, op2.mem, op1.regSize === 64, pref);
                return;
            }

            if (op1.type === 'Memory' && op2.type === 'Register') {
                const opc = op2.regSize === 8 ? 0x88 : 0x89;
                const pref = op2.regSize === 16 ? 0x66 : 0;
                this.encodeMemAccess(buf, prog, opc, op2.regIndex, op1.mem, op2.regSize === 64, pref);
                return;
            }

            if (op1.type === 'Memory' && op2.type === 'Immediate') {
                const opc = op1.mem.size === 8 ? 0xC6 : 0xC7;
                this.encodeMemAccess(buf, prog, opc, 0, op1.mem, op1.mem.size === 64);
                const imm = Number(op2.imm);
                if (op1.mem.size === 8) buf.push(imm & 0xFF);
                else {
                    const b = Buffer.alloc(4); b.writeInt32LE(imm);
                    for (let i = 0; i < 4; i++) buf.push(b[i]);
                }
                return;
            }
        }

        const aluExt = { add: 0, or: 1, and: 4, sub: 5, xor: 6, cmp: 7, test: 0 };
        if (aluExt[m] !== undefined && ops.length === 2) {
            const op1 = ops[0];
            const op2 = ops[1];
            const ext = aluExt[m];

            if (op1.type === 'Register' && op2.type === 'Register') {
                const dst = op1.regIndex;
                const src = op2.regIndex;
                const sz = op1.regSize;
                if (m === 'test') {
                    if (sz === 64) buf.push(this.makeRex(1, src >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0));
                    else if (src >= 8 || dst >= 8) buf.push(this.makeRex(0, src >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0));
                    buf.push(sz === 8 ? 0x84 : 0x85, this.makeModRm(3, src & 7, dst & 7));
                } else {
                    const baseOp = ext * 8 + 1;
                    if (sz === 64) buf.push(this.makeRex(1, src >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0));
                    else if (src >= 8 || dst >= 8) buf.push(this.makeRex(0, src >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0));
                    buf.push(baseOp, this.makeModRm(3, src & 7, dst & 7));
                }
                return;
            }

            if (op1.type === 'Register' && op2.type === 'Immediate') {
                const dst = op1.regIndex;
                const sz = op1.regSize;
                const imm = Number(op2.imm);
                if (m === 'test') {
                    if (sz === 64) buf.push(this.makeRex(1, 0, 0, dst >= 8 ? 1 : 0));
                    else if (dst >= 8) buf.push(this.makeRex(0, 0, 0, 1));
                    buf.push(sz === 8 ? 0xF6 : 0xF7, this.makeModRm(3, 0, dst & 7));
                    if (sz === 8) buf.push(imm & 0xFF);
                    else {
                        const b = Buffer.alloc(4); b.writeInt32LE(imm);
                        for (let i = 0; i < 4; i++) buf.push(b[i]);
                    }
                } else {
                    const isShort = (imm >= -128 && imm <= 127);
                    if (sz === 64) buf.push(this.makeRex(1, 0, 0, dst >= 8 ? 1 : 0));
                    else if (dst >= 8) buf.push(this.makeRex(0, 0, 0, 1));
                    if (isShort) {
                        buf.push(0x83, this.makeModRm(3, ext, dst & 7), imm & 0xFF);
                    } else {
                        buf.push(0x81, this.makeModRm(3, ext, dst & 7));
                        const b = Buffer.alloc(4); b.writeInt32LE(imm);
                        for (let i = 0; i < 4; i++) buf.push(b[i]);
                    }
                }
                return;
            }

            if (op1.type === 'Register' && op2.type === 'Memory') {
                this.encodeMemAccess(buf, prog, ext * 8 + 3, op1.regIndex, op2.mem, op1.regSize === 64);
                return;
            }
            if (op1.type === 'Memory' && op2.type === 'Register') {
                this.encodeMemAccess(buf, prog, ext * 8 + 1, op2.regIndex, op1.mem, op2.regSize === 64);
                return;
            }
        }

        const unaryExt = { inc: 0, dec: 1, not: 2, neg: 3, idiv: 7 };
        if (unaryExt[m] !== undefined && ops.length === 1) {
            const ext = unaryExt[m];
            if (ops[0].type === 'Register') {
                const r = ops[0].regIndex;
                if (ops[0].regSize === 64) buf.push(this.makeRex(1, 0, 0, r >= 8 ? 1 : 0));
                else if (r >= 8) buf.push(this.makeRex(0, 0, 0, 1));
                buf.push((m === 'inc' || m === 'dec') ? 0xFF : 0xF7, this.makeModRm(3, ext, r & 7));
                return;
            }
        }

        const shiftExt = { shl: 4, shr: 5, sar: 7 };
        if (shiftExt[m] !== undefined && ops.length === 2 && ops[0].type === 'Register') {
            const r = ops[0].regIndex;
            const ext = shiftExt[m];
            if (ops[0].regSize === 64) buf.push(this.makeRex(1, 0, 0, r >= 8 ? 1 : 0));
            else if (r >= 8) buf.push(this.makeRex(0, 0, 0, 1));

            if (ops[1].type === 'Immediate') {
                const cnt = Number(ops[1].imm);
                if (cnt === 1) buf.push(0xD1, this.makeModRm(3, ext, r & 7));
                else buf.push(0xC1, this.makeModRm(3, ext, r & 7), cnt & 0xFF);
                return;
            } else if (ops[1].type === 'Register' && ops[1].reg === 'cl') {
                buf.push(0xD3, this.makeModRm(3, ext, r & 7));
                return;
            }
        }

        if (m === 'imul') {
            if (ops.length === 2 && ops[0].type === 'Register' && ops[1].type === 'Register') {
                const dst = ops[0].regIndex;
                const src = ops[1].regIndex;
                buf.push(this.makeRex(1, dst >= 8 ? 1 : 0, 0, src >= 8 ? 1 : 0), 0x0F, 0xAF, this.makeModRm(3, dst & 7, src & 7));
                return;
            } else if (ops.length === 2 && ops[0].type === 'Register' && ops[1].type === 'Immediate') {
                const dst = ops[0].regIndex;
                buf.push(this.makeRex(1, dst >= 8 ? 1 : 0, 0, dst >= 8 ? 1 : 0), 0x69, this.makeModRm(3, dst & 7, dst & 7));
                const b = Buffer.alloc(4); b.writeInt32LE(Number(ops[1].imm));
                for (let i = 0; i < 4; i++) buf.push(b[i]);
                return;
            }
        }

        if (m === 'xchg' && ops.length === 2 && ops[0].type === 'Register' && ops[1].type === 'Register') {
            const r1 = ops[0].regIndex;
            const r2 = ops[1].regIndex;
            buf.push(this.makeRex(1, r1 >= 8 ? 1 : 0, 0, r2 >= 8 ? 1 : 0), 0x87, this.makeModRm(3, r1 & 7, r2 & 7));
            return;
        }

        const setccMap = { sete: 0x94, setz: 0x94, setne: 0x95, setnz: 0x95, setl: 0x9C, setle: 0x9E, setg: 0x9F, setge: 0x9D, setb: 0x92, seta: 0x97 };
        if (setccMap[m] && ops.length === 1 && ops[0].type === 'Register') {
            const r = ops[0].regIndex;
            if (r >= 4) buf.push(this.makeRex(0, 0, 0, r >= 8 ? 1 : 0));
            buf.push(0x0F, setccMap[m], this.makeModRm(3, 0, r & 7));
            return;
        }

        const cmovMap = { cmove: 0x44, cmovz: 0x44, cmovne: 0x45, cmovl: 0x4C, cmovg: 0x4F, cmovge: 0x4D, cmovle: 0x4E, cmovb: 0x42, cmova: 0x47 };
        if (cmovMap[m] && ops.length === 2 && ops[0].type === 'Register') {
            const dst = ops[0].regIndex;
            if (ops[1].type === 'Register') {
                const src = ops[1].regIndex;
                buf.push(this.makeRex(1, dst >= 8 ? 1 : 0, 0, src >= 8 ? 1 : 0), 0x0F, cmovMap[m], this.makeModRm(3, dst & 7, src & 7));
                return;
            } else if (ops[1].type === 'Memory') {
                this.encodeMemAccess(buf, prog, cmovMap[m], dst, ops[1].mem, true, 0, true);
                return;
            }
        }

        // SIMD SSE
        const sseOpcodes = {
            movups: [0x00, 0x10], movaps: [0x00, 0x28],
            xorps:  [0x00, 0x57], addps:  [0x00, 0x58],
            subps:  [0x00, 0x5C], mulps:  [0x00, 0x59], divps: [0x00, 0x5E],
            pxor:   [0x66, 0xEF], movdqa: [0x66, 0x6F], movdqu: [0xF3, 0x6F]
        };

        if (sseOpcodes[m] && ops.length === 2) {
            const [mand, code] = sseOpcodes[m];
            if (ops[0].type === 'Register' && ops[1].type === 'Register') {
                if (mand) buf.push(mand);
                const dst = ops[0].regIndex;
                const src = ops[1].regIndex;
                if (dst >= 8 || src >= 8) buf.push(this.makeRex(0, dst >= 8 ? 1 : 0, 0, src >= 8 ? 1 : 0));
                buf.push(0x0F, code, this.makeModRm(3, dst & 7, src & 7));
                return;
            } else if (ops[0].type === 'Register' && ops[1].type === 'Memory') {
                this.encodeMemAccess(buf, prog, code, ops[0].regIndex, ops[1].mem, false, mand, true);
                return;
            } else if (ops[0].type === 'Memory' && ops[1].type === 'Register') {
                let storeCode = code;
                if (m === 'movups') storeCode = 0x11;
                else if (m === 'movaps') storeCode = 0x29;
                else if (m === 'movdqa' || m === 'movdqu') storeCode = 0x7F;
                this.encodeMemAccess(buf, prog, storeCode, ops[1].regIndex, ops[0].mem, false, mand, true);
                return;
            }
        }

        throw new Error(`Unsupported instruction: '${s.mnemonic}' at line ${s.line}`);
    }
}

// --- 6. Builders (PE, ELF, Mach-O, Flat Bin) ---
function alignUp(val, align) {
    return (val + align - 1) & ~(align - 1);
}

function buildPE(prog) {
    const fileAlign = 512;
    const sectionAlign = 4096;
    const textRva = 4096;

    const rawTextSize = alignUp(Math.max(1, prog.textBytes.length), fileAlign);
    const virtTextSize = alignUp(rawTextSize, sectionAlign);
    const idataRva = textRva + virtTextSize;

    const imports = [
        "ExitProcess", "GetStdHandle", "WriteFile", "WriteConsoleA", "ReadFile",
        "CreateFileA", "CloseHandle", "GetCommandLineA", "Sleep", "GetTickCount64",
        "VirtualAlloc", "VirtualFree", "HeapAlloc", "HeapFree", "GetProcessHeap"
    ];
    for (const r of prog.relocations) {
        if (r.type === 'IMPORT_CALL' && !imports.includes(r.targetSymbol)) imports.push(r.targetSymbol);
    }

    const idtSize = 40;
    const iltOffset = idtSize;
    const iltSize = (imports.length + 1) * 8;
    const iatOffset = iltOffset + iltSize;
    const iatSize = iltSize;
    const dllNameOffset = iatOffset + iatSize;
    const dllName = Buffer.from("KERNEL32.DLL\0", "ascii");
    const hintsOffset = dllNameOffset + alignUp(dllName.length, 8);

    const idataPayload = [];
    const hintRvas = [];
    const hintBytes = [];

    for (const fn of imports) {
        hintRvas.push(idataRva + hintsOffset + hintBytes.length);
        hintBytes.push(0, 0); // hint 0
        const b = Buffer.from(fn + '\0', 'ascii');
        for (let i = 0; i < b.length; i++) hintBytes.push(b[i]);
        if (hintBytes.length % 2 !== 0) hintBytes.push(0);
    }

    // IDT
    const bIdt = Buffer.alloc(40);
    bIdt.writeInt32LE(idataRva + iltOffset, 0);
    bIdt.writeInt32LE(idataRva + dllNameOffset, 12);
    bIdt.writeInt32LE(idataRva + iatOffset, 16);
    for (let i = 0; i < 40; i++) idataPayload.push(bIdt[i]);

    // ILT
    for (let i = 0; i < imports.length; i++) {
        const b = Buffer.alloc(8); b.writeBigInt64LE(BigInt(hintRvas[i]));
        for (let j = 0; j < 8; j++) idataPayload.push(b[j]);
    }
    for (let j = 0; j < 8; j++) idataPayload.push(0);

    // IAT
    const iatSymbolRvas = {};
    for (let i = 0; i < imports.length; i++) {
        iatSymbolRvas[imports[i]] = idataRva + iatOffset + i * 8;
        const b = Buffer.alloc(8); b.writeBigInt64LE(BigInt(hintRvas[i]));
        for (let j = 0; j < 8; j++) idataPayload.push(b[j]);
    }
    for (let j = 0; j < 8; j++) idataPayload.push(0);

    // DLL name & padding
    for (let i = 0; i < dllName.length; i++) idataPayload.push(dllName[i]);
    while (idataPayload.length < hintsOffset) idataPayload.push(0);

    // Hints
    for (let i = 0; i < hintBytes.length; i++) idataPayload.push(hintBytes[i]);

    const rawIdataSize = alignUp(idataPayload.length, fileAlign);
    const virtIdataSize = alignUp(rawIdataSize, sectionAlign);

    const dataRva = idataRva + virtIdataSize;
    const totalDataBytes = prog.dataBytes.length + prog.bssSize;
    const hasData = totalDataBytes > 0;
    const rawDataSize = hasData ? alignUp(prog.dataBytes.length, fileAlign) : 0;
    const virtDataSize = hasData ? alignUp(Math.max(totalDataBytes, rawDataSize), sectionAlign) : 0;

    // Relocations
    for (const r of prog.relocations) {
        const curRva = textRva + r.offset + 4;
        if (r.type === 'IMPORT_CALL') {
            const targetRva = iatSymbolRvas[r.targetSymbol] || idataRva;
            const delta = targetRva - curRva;
            const b = Buffer.alloc(4); b.writeInt32LE(delta);
            for (let i = 0; i < 4; i++) prog.textBytes[r.offset + i] = b[i];
        } else if (r.type === 'REL32' || r.type === 'RIP_REL32') {
            const sym = prog.symbols[r.targetSymbol];
            if (sym) {
                const symBase = sym.section === '.text' ? textRva : (sym.section === '.bss' ? (dataRva + prog.dataBytes.length) : dataRva);
                const targetRva = symBase + sym.offset;
                const delta = (targetRva - curRva) + (r.addend || 0);
                const b = Buffer.alloc(4); b.writeInt32LE(delta);
                for (let i = 0; i < 4; i++) prog.textBytes[r.offset + i] = b[i];
            }
        }
    }

    const headersRaw = 1024;
    const textRawOffset = headersRaw;
    const idataRawOffset = textRawOffset + rawTextSize;
    const dataRawOffset = idataRawOffset + rawIdataSize;

    const totalImageSize = hasData ? (dataRva + virtDataSize) : (idataRva + virtIdataSize);
    const totalFileSize = hasData ? (dataRawOffset + rawDataSize) : (idataRawOffset + rawIdataSize);

    const pe = Buffer.alloc(totalFileSize);

    // DOS Header
    pe[0] = 0x4D; pe[1] = 0x5A; // MZ
    pe.writeInt32LE(128, 60);

    // PE signature
    pe[128] = 0x50; pe[129] = 0x45; // PE

    // COFF
    const numSections = hasData ? 3 : 2;
    pe.writeUInt16LE(0x8664, 132); // AMD64
    pe.writeUInt16LE(numSections, 134);
    pe.writeUInt16LE(240, 148);
    pe.writeUInt16LE(0x0022, 150);

    // Optional Header
    pe.writeUInt16LE(0x020B, 152); // PE32+
    pe[154] = 1;
    pe.writeInt32LE(rawTextSize, 156);
    pe.writeInt32LE(rawIdataSize + (hasData ? rawDataSize : 0), 160);

    const entryOffset = prog.symbols[prog.entrySymbol] ? prog.symbols[prog.entrySymbol].offset : 0;
    pe.writeInt32LE(textRva + entryOffset, 168);
    pe.writeInt32LE(textRva, 172);

    pe.writeBigInt64LE(0x140000000n, 176);
    pe.writeInt32LE(sectionAlign, 184);
    pe.writeInt32LE(fileAlign, 188);
    pe.writeInt16LE(6, 192); pe.writeInt16LE(6, 200);
    pe.writeInt32LE(totalImageSize, 208);
    pe.writeInt32LE(headersRaw, 212);
    pe.writeInt16LE(3, 220); // Console CUI
    pe.writeUInt16LE(0x8160, 222);
    pe.writeBigInt64LE(0x100000n, 224);
    pe.writeBigInt64LE(0x1000n, 232);
    pe.writeBigInt64LE(0x100000n, 240);
    pe.writeBigInt64LE(0x1000n, 248);
    pe.writeInt32LE(16, 260);

    // Data Directories
    pe.writeInt32LE(idataRva, 264 + 8);
    pe.writeInt32LE(40, 264 + 12);
    pe.writeInt32LE(idataRva + iatOffset, 264 + 96);
    pe.writeInt32LE(iatSize, 264 + 100);

    // .text section header (offset 392)
    Buffer.from('.text\0\0\0').copy(pe, 392);
    pe.writeInt32LE(prog.textBytes.length, 400);
    pe.writeInt32LE(textRva, 404);
    pe.writeInt32LE(rawTextSize, 408);
    pe.writeInt32LE(textRawOffset, 412);
    pe.writeInt32LE(0x60000020, 428);

    // .idata section header (offset 432)
    Buffer.from('.idata\0\0').copy(pe, 432);
    pe.writeInt32LE(idataPayload.length, 440);
    pe.writeInt32LE(idataRva, 444);
    pe.writeInt32LE(rawIdataSize, 448);
    pe.writeInt32LE(idataRawOffset, 452);
    pe.writeInt32LE(uncheckedInt(0xC0000040), 468);

    if (hasData) {
        Buffer.from('.data\0\0\0').copy(pe, 472);
        pe.writeInt32LE(totalDataBytes, 480);
        pe.writeInt32LE(dataRva, 484);
        pe.writeInt32LE(rawDataSize, 488);
        pe.writeInt32LE(dataRawOffset, 492);
        pe.writeInt32LE(uncheckedInt(0xC0000040), 508);
    }

    Buffer.from(prog.textBytes).copy(pe, textRawOffset);
    Buffer.from(idataPayload).copy(pe, idataRawOffset);
    if (hasData && prog.dataBytes.length > 0) {
        Buffer.from(prog.dataBytes).copy(pe, dataRawOffset);
    }

    return pe;
}

function uncheckedInt(val) {
    return (val > 0x7FFFFFFF) ? (val - 0x100000000) : val;
}

function buildELF(prog) {
    const baseVAddr = 0x400000n;
    const pageSize = 0x1000;
    const hasData = (prog.dataBytes.length + prog.bssSize) > 0;
    const numPhdrs = hasData ? 2 : 1;

    const textFileOffset = pageSize;
    const textVAddr = baseVAddr + BigInt(textFileOffset);
    const rawTextSize = Math.max(1, prog.textBytes.length);
    const alignedTextSize = alignUp(rawTextSize, pageSize);

    const dataFileOffset = textFileOffset + alignedTextSize;
    const dataVAddr = textVAddr + BigInt(alignedTextSize);
    const rawDataSize = prog.dataBytes.length;
    const totalDataMemSize = rawDataSize + prog.bssSize;

    // Relocations
    for (const r of prog.relocations) {
        const curVAddr = textVAddr + BigInt(r.offset + 4);
        if (r.type === 'REL32' || r.type === 'RIP_REL32') {
            const sym = prog.symbols[r.targetSymbol];
            if (sym) {
                const symBase = sym.section === '.text' ? textVAddr : (sym.section === '.bss' ? (dataVAddr + BigInt(rawDataSize)) : dataVAddr);
                const targetVAddr = symBase + BigInt(sym.offset);
                const delta = Number((targetVAddr - curVAddr) + BigInt(r.addend || 0));
                const b = Buffer.alloc(4); b.writeInt32LE(delta);
                for (let i = 0; i < 4; i++) prog.textBytes[r.offset + i] = b[i];
            }
        }
    }

    let entryAddr = textVAddr;
    if (prog.symbols[prog.entrySymbol]) entryAddr = textVAddr + BigInt(prog.symbols[prog.entrySymbol].offset);
    else if (prog.symbols['_start']) entryAddr = textVAddr + BigInt(prog.symbols['_start'].offset);
    else if (prog.symbols['main']) entryAddr = textVAddr + BigInt(prog.symbols['main'].offset);

    const totalFileSize = hasData ? (dataFileOffset + rawDataSize) : (textFileOffset + rawTextSize);
    const elf = Buffer.alloc(totalFileSize);

    // ELF header (64 bytes)
    elf[0] = 0x7F; elf[1] = 0x45; elf[2] = 0x4C; elf[3] = 0x46; // \x7FELF
    elf[4] = 2; // 64-bit
    elf[5] = 1; // Little endian
    elf[6] = 1; // Version
    elf[7] = 0; // SYSV
    elf.writeInt16LE(2, 16);     // ET_EXEC
    elf.writeInt16LE(0x3E, 18);  // EM_X86_64
    elf.writeInt32LE(1, 20);     // EV_CURRENT
    elf.writeBigUInt64LE(entryAddr, 24);
    elf.writeBigUInt64LE(64n, 32); // phoff
    elf.writeInt16LE(64, 52);    // ehsize
    elf.writeInt16LE(56, 54);    // phentsize
    elf.writeInt16LE(numPhdrs, 56);

    // PHDR 1: .text (RX)
    let p1 = 64;
    elf.writeInt32LE(1, p1 + 0);  // PT_LOAD
    elf.writeInt32LE(5, p1 + 4);  // PF_R | PF_X
    elf.writeBigUInt64LE(BigInt(textFileOffset), p1 + 8);
    elf.writeBigUInt64LE(textVAddr, p1 + 16);
    elf.writeBigUInt64LE(textVAddr, p1 + 24);
    elf.writeBigUInt64LE(BigInt(rawTextSize), p1 + 32);
    elf.writeBigUInt64LE(BigInt(rawTextSize), p1 + 40);
    elf.writeBigUInt64LE(BigInt(pageSize), p1 + 48);

    // PHDR 2: .data (RW)
    if (hasData) {
        let p2 = 64 + 56;
        elf.writeInt32LE(1, p2 + 0); // PT_LOAD
        elf.writeInt32LE(6, p2 + 4); // PF_R | PF_W
        elf.writeBigUInt64LE(BigInt(dataFileOffset), p2 + 8);
        elf.writeBigUInt64LE(dataVAddr, p2 + 16);
        elf.writeBigUInt64LE(dataVAddr, p2 + 24);
        elf.writeBigUInt64LE(BigInt(rawDataSize), p2 + 32);
        elf.writeBigUInt64LE(BigInt(totalDataMemSize), p2 + 40);
        elf.writeBigUInt64LE(BigInt(pageSize), p2 + 48);
    }

    Buffer.from(prog.textBytes).copy(elf, textFileOffset);
    if (hasData && rawDataSize > 0) {
        Buffer.from(prog.dataBytes).copy(elf, dataFileOffset);
    }

    return elf;
}

function buildMachO(prog) {
    const pageZeroSize = 0x100000000n;
    const textBaseVAddr = 0x100000000n;
    const pageSize = 0x1000;
    const hasData = (prog.dataBytes.length + prog.bssSize) > 0;

    const rawTextSize = Math.max(1, prog.textBytes.length);
    const textFileOffset = pageSize;
    const textVAddr = textBaseVAddr + BigInt(textFileOffset);
    const alignedTextSize = alignUp(textFileOffset + rawTextSize, pageSize);

    const dataFileOffset = alignedTextSize;
    const dataVAddr = textBaseVAddr + BigInt(dataFileOffset);
    const rawDataSize = prog.dataBytes.length;
    const totalDataMemSize = rawDataSize + prog.bssSize;

    // Relocations
    for (const r of prog.relocations) {
        const curVAddr = textVAddr + BigInt(r.offset + 4);
        if (r.type === 'REL32' || r.type === 'RIP_REL32') {
            const sym = prog.symbols[r.targetSymbol];
            if (sym) {
                const symBase = sym.section === '.text' ? textVAddr : (sym.section === '.bss' ? (dataVAddr + BigInt(rawDataSize)) : dataVAddr);
                const targetVAddr = symBase + BigInt(sym.offset);
                const delta = Number((targetVAddr - curVAddr) + BigInt(r.addend || 0));
                const b = Buffer.alloc(4); b.writeInt32LE(delta);
                for (let i = 0; i < 4; i++) prog.textBytes[r.offset + i] = b[i];
            }
        }
    }

    let entryAddr = textVAddr;
    if (prog.symbols[prog.entrySymbol]) entryAddr = textVAddr + BigInt(prog.symbols[prog.entrySymbol].offset);
    else if (prog.symbols['main']) entryAddr = textVAddr + BigInt(prog.symbols['main'].offset);

    const ncmds = hasData ? 4 : 3;
    const sizeofcmds = 72 + 152 + (hasData ? 152 : 0) + 184;
    const totalFileSize = hasData ? (dataFileOffset + rawDataSize) : (textFileOffset + rawTextSize);
    const macho = Buffer.alloc(totalFileSize);

    // Mach Header
    macho.writeUInt32LE(0xFEEDFACF, 0); // MH_MAGIC_64
    macho.writeUInt32LE(0x01000007, 4); // CPU_TYPE_X86_64
    macho.writeUInt32LE(0x00000003, 8); // CPU_SUBTYPE_X86_64_ALL
    macho.writeUInt32LE(2, 12);          // MH_EXECUTE
    macho.writeUInt32LE(ncmds, 16);
    macho.writeUInt32LE(sizeofcmds, 20);
    macho.writeUInt32LE(0x00200085, 24);

    let cmdOff = 32;

    // LC_SEGMENT_64 (__PAGEZERO)
    macho.writeUInt32LE(0x19, cmdOff + 0);
    macho.writeUInt32LE(72, cmdOff + 4);
    Buffer.from('__PAGEZERO\0').copy(macho, cmdOff + 8);
    macho.writeBigUInt64LE(pageZeroSize, cmdOff + 32);
    cmdOff += 72;

    // LC_SEGMENT_64 (__TEXT)
    macho.writeUInt32LE(0x19, cmdOff + 0);
    macho.writeUInt32LE(152, cmdOff + 4);
    Buffer.from('__TEXT\0').copy(macho, cmdOff + 8);
    macho.writeBigUInt64LE(textBaseVAddr, cmdOff + 24);
    macho.writeBigUInt64LE(BigInt(alignedTextSize), cmdOff + 32);
    macho.writeBigUInt64LE(BigInt(alignedTextSize), cmdOff + 48);
    macho.writeInt32LE(7, cmdOff + 56);
    macho.writeInt32LE(5, cmdOff + 60);
    macho.writeInt32LE(1, cmdOff + 64);

    // Section __text
    let st = cmdOff + 72;
    Buffer.from('__text\0').copy(macho, st + 0);
    Buffer.from('__TEXT\0').copy(macho, st + 16);
    macho.writeBigUInt64LE(textVAddr, st + 32);
    macho.writeBigUInt64LE(BigInt(rawTextSize), st + 40);
    macho.writeInt32LE(textFileOffset, st + 48);
    macho.writeInt32LE(4, st + 52);
    macho.writeUInt32LE(0x80000400, st + 64);
    cmdOff += 152;

    // LC_SEGMENT_64 (__DATA)
    if (hasData) {
        macho.writeUInt32LE(0x19, cmdOff + 0);
        macho.writeUInt32LE(152, cmdOff + 4);
        Buffer.from('__DATA\0').copy(macho, cmdOff + 8);
        macho.writeBigUInt64LE(dataVAddr, cmdOff + 24);
        macho.writeBigUInt64LE(BigInt(alignUp(totalDataMemSize, pageSize)), cmdOff + 32);
        macho.writeBigUInt64LE(BigInt(dataFileOffset), cmdOff + 40);
        macho.writeBigUInt64LE(BigInt(rawDataSize), cmdOff + 48);
        macho.writeInt32LE(7, cmdOff + 56);
        macho.writeInt32LE(3, cmdOff + 60);
        macho.writeInt32LE(1, cmdOff + 64);

        let sd = cmdOff + 72;
        Buffer.from('__data\0').copy(macho, sd + 0);
        Buffer.from('__DATA\0').copy(macho, sd + 16);
        macho.writeBigUInt64LE(dataVAddr, sd + 32);
        macho.writeBigUInt64LE(BigInt(rawDataSize), sd + 40);
        macho.writeInt32LE(dataFileOffset, sd + 48);
        macho.writeInt32LE(3, sd + 52);
        cmdOff += 152;
    }

    // LC_UNIXTHREAD
    macho.writeUInt32LE(0x5, cmdOff + 0);
    macho.writeUInt32LE(184, cmdOff + 4);
    macho.writeUInt32LE(4, cmdOff + 8);  // x86_THREAD_STATE64
    macho.writeUInt32LE(42, cmdOff + 12);
    macho.writeBigUInt64LE(entryAddr, cmdOff + 16 + 128); // rip

    Buffer.from(prog.textBytes).copy(macho, textFileOffset);
    if (hasData && rawDataSize > 0) {
        Buffer.from(prog.dataBytes).copy(macho, dataFileOffset);
    }

    return macho;
}

function buildFlatBin(prog) {
    const raw = Buffer.alloc(prog.textBytes.length + prog.dataBytes.length);
    Buffer.from(prog.textBytes).copy(raw, 0);
    Buffer.from(prog.dataBytes).copy(raw, prog.textBytes.length);
    return raw;
}

// --- 7. Main RawX API ---
const RawX = {
    version: '2.0.0',

    async compile(source, options = {}) {
        const target = options.target || (process.platform === 'win32' ? 'windows' : 'linux');
        const format = options.format || (target === 'windows' ? 'pe' : (target === 'macos' ? 'macho' : 'elf'));
        const rootDir = options.rootDir || process.cwd();

        const prep = new Preprocessor(target, rootDir);
        const preprocessed = await prep.process(source, options.filePath || null);

        const lexer = new Lexer(preprocessed);
        const tokens = lexer.tokenize();

        const parser = new Parser(tokens);
        const ast = parser.parse();

        const encoder = new Encoder(target);
        const prog = encoder.encode(ast);

        if (format === 'pe') return buildPE(prog);
        if (format === 'elf') return buildELF(prog);
        if (format === 'macho') return buildMachO(prog);
        if (format === 'bin') return buildFlatBin(prog);
        throw new Error(`Unknown format: '${format}'`);
    },

    async compileUrl(url, options = {}) {
        if (!/^https?:\/\//i.test(url)) {
            throw new Error(`Invalid URL scheme in: ${url}`);
        }
        if (typeof fetch === 'undefined') {
            throw new Error('Global fetch API is required to compile from remote URL.');
        }

        const resp = await fetch(url);
        if (!resp.ok) throw new Error(`HTTP ${resp.status} fetching remote RawX source: ${url}`);
        const src = await resp.text();

        // Local cache
        const cacheDir = path.join(process.cwd(), '.rawx_cache');
        if (!fs.existsSync(cacheDir)) fs.mkdirSync(cacheDir, { recursive: true });
        const urlObj = new URL(url);
        let filename = path.basename(urlObj.pathname);
        if (!filename || !filename.endsWith('.rx')) {
            filename = `remote_${Math.abs(url.split('').reduce((a, b) => ((a << 5) - a) + b.charCodeAt(0), 0))}.rx`;
        }
        const cachedPath = path.join(cacheDir, filename);
        fs.writeFileSync(cachedPath, src, 'utf8');

        const target = options.target || (process.platform === 'win32' ? 'windows' : (process.platform === 'darwin' ? 'macos' : 'linux'));
        const format = options.format || (target === 'windows' ? 'pe' : (target === 'macos' ? 'macho' : 'elf'));

        const buf = await this.compile(src, {
            ...options,
            target,
            format,
            filePath: cachedPath,
            rootDir: cacheDir
        });

        let outPath = options.outPath;
        if (!outPath) {
            const ext = format === 'pe' ? '.exe' : (format === 'elf' ? '.elf' : (format === 'macho' ? '.macho' : '.bin'));
            const base = path.basename(filename, path.extname(filename));
            outPath = path.join(process.cwd(), 'bin', base + ext);
        }

        const outDir = path.dirname(outPath);
        if (!fs.existsSync(outDir)) fs.mkdirSync(outDir, { recursive: true });
        fs.writeFileSync(outPath, buf);
        if (process.platform !== 'win32') {
            try { fs.chmodSync(outPath, 0o755); } catch {}
        }
        return outPath;
    },

    async compileFile(inputPath, options = {}) {
        if (/^https?:\/\//i.test(inputPath)) {
            return await this.compileUrl(inputPath, options);
        }

        const src = fs.readFileSync(inputPath, 'utf8');
        const target = options.target || (process.platform === 'win32' ? 'windows' : (process.platform === 'darwin' ? 'macos' : 'linux'));
        const format = options.format || (target === 'windows' ? 'pe' : (target === 'macos' ? 'macho' : 'elf'));

        const buf = await this.compile(src, {
            ...options,
            target,
            format,
            filePath: path.resolve(inputPath),
            rootDir: path.dirname(path.resolve(inputPath))
        });

        let outPath = options.outPath;
        if (!outPath) {
            const ext = format === 'pe' ? '.exe' : (format === 'elf' ? '.elf' : (format === 'macho' ? '.macho' : '.bin'));
            const base = path.basename(inputPath, path.extname(inputPath));
            outPath = path.join(path.dirname(inputPath), '..', 'bin', base + ext);
            if (!fs.existsSync(path.dirname(outPath))) outPath = path.join(process.cwd(), 'bin', base + ext);
        }

        const outDir = path.dirname(outPath);
        if (!fs.existsSync(outDir)) fs.mkdirSync(outDir, { recursive: true });
        fs.writeFileSync(outPath, buf);
        if (process.platform !== 'win32') {
            try { fs.chmodSync(outPath, 0o755); } catch {}
        }
        return outPath;
    },

    async runUrl(url, options = {}) {
        const binPath = await this.compileUrl(url, options);
        return await this.run(binPath, options);
    },

    async run(sourceOrPath, options = {}) {
        let binPath = sourceOrPath;

        if (typeof sourceOrPath === 'string' && /^https?:\/\//i.test(sourceOrPath)) {
            return await this.runUrl(sourceOrPath, options);
        }

        if (typeof sourceOrPath === 'string' && (sourceOrPath.endsWith('.rx') || !fs.existsSync(sourceOrPath))) {
            if (fs.existsSync(sourceOrPath)) {
                binPath = await this.compileFile(sourceOrPath, options);
            } else {
                const target = options.target || (process.platform === 'win32' ? 'windows' : (process.platform === 'darwin' ? 'macos' : 'linux'));
                const format = options.format || (target === 'windows' ? 'pe' : (target === 'macos' ? 'macho' : 'elf'));
                const ext = format === 'pe' ? '.exe' : (format === 'elf' ? '.elf' : (format === 'macho' ? '.macho' : '.bin'));
                const tmpBin = path.join(process.cwd(), 'bin', `tmp_${Date.now()}_${Math.floor(Math.random()*10000)}${ext}`);
                const buf = await this.compile(sourceOrPath, { ...options, target, format });
                const binDir = path.dirname(tmpBin);
                if (!fs.existsSync(binDir)) fs.mkdirSync(binDir, { recursive: true });
                fs.writeFileSync(tmpBin, buf);
                if (process.platform !== 'win32') {
                    try { fs.chmodSync(tmpBin, 0o755); } catch {}
                }
                binPath = tmpBin;
            }
        }

        const res = spawnSync(binPath, options.args || [], {
            stdio: options.stdio || 'pipe',
            input: options.input,
            cwd: options.cwd || process.cwd(),
            env: { ...process.env, ...(options.env || {}) },
            timeout: options.timeout || 30000,
            encoding: 'utf8'
        });

        return {
            exitCode: res.status !== null ? res.status : -1,
            stdout: res.stdout || '',
            stderr: res.stderr || ''
        };
    }
};

// --- 8. CLI Runner ---
if (require.main === module) {
    (async () => {
        const args = process.argv.slice(2);
        if (args.length === 0 || args.includes('-h') || args.includes('--help')) {
            console.log(`========================================================`);
            console.log(` [RawX Node.js Engine] Pure JavaScript Compiler & Runner`);
            console.log(` Version: ${RawX.version} | AMD64 Universal Multi-Target`);
            console.log(`========================================================`);
            console.log(` Usage: node rawx.js <file.rx | URL> [options]`);
            console.log(`   -o <path>          Output binary path`);
            console.log(`   -t, --target <os>  Target OS: windows, linux, macos`);
            console.log(`   -f, --format <fmt> Format: pe, elf, macho, bin`);
            console.log(`   -r, --run          Immediately execute compiled binary`);
            console.log(``);
            console.log(` Examples:`);
            console.log(`   node rawx.js examples/02_hello.rx -r`);
            console.log(`   node rawx.js https://raw.githubusercontent.com/.../02_hello.rx -r`);
            console.log(`========================================================`);
            process.exit(0);
        }

        let src = null;
        let out = null;
        let target = process.platform === 'win32' ? 'windows' : (process.platform === 'darwin' ? 'macos' : 'linux');
        let format = null;
        let doRun = false;

        for (let i = 0; i < args.length; i++) {
            if (args[i] === '-o' && i + 1 < args.length) out = args[++i];
            else if ((args[i] === '-t' || args[i] === '--target') && i + 1 < args.length) target = args[++i];
            else if ((args[i] === '-f' || args[i] === '--format') && i + 1 < args.length) format = args[++i];
            else if (args[i] === '-r' || args[i] === '--run') doRun = true;
            else if (!args[i].startsWith('-') && !src) src = args[i];
        }

        if (!src) {
            console.error('[Error] No source file or URL specified.');
            process.exit(1);
        }

        const t0 = Date.now();
        const isUrl = /^https?:\/\//i.test(src);
        if (isUrl) {
            console.log(`[*] Fetching remote RawX source from URL: ${src}`);
        } else {
            console.log(`[*] Compiling '${src}' with RawX JavaScript Engine...`);
        }

        const binPath = await RawX.compileFile(src, { target, format, outPath: out });
        console.log(`[+] Standalone binary synthesized: ${binPath} (${Date.now() - t0}ms)`);

        if (doRun) {
            console.log(`\n[Running ${binPath}...]\n--------------------------------------------------------`);
            const res = await RawX.run(binPath, { stdio: 'inherit' });
            console.log(`--------------------------------------------------------\n[Exit Code: ${res.exitCode}]`);
        }
    })().catch(err => {
        console.error('[Error]', err.message);
        process.exit(1);
    });
}

module.exports = { RawX, Lexer, Parser, Encoder, Preprocessor };
