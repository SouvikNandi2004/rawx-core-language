<div align="center">

# ⚡ RawX: Bare-Metal Systems Programming Language

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=for-the-badge)](https://opensource.org/licenses/MIT)
[![Architecture: AMD64 / x86-64](https://img.shields.io/badge/Arch-AMD64%20%2F%20x86--64-red.svg?style=for-the-badge)](#)
[![Targets: Windows | Linux | macOS](https://img.shields.io/badge/Targets-Windows%20%7C%20Linux%20%7C%20macOS-success.svg?style=for-the-badge)](#)
[![SIMD: SSE & AVX](https://img.shields.io/badge/SIMD-XMM0..XMM15-purple.svg?style=for-the-badge)](#)
[![Runtime: Zero Dependency](https://img.shields.io/badge/Runtime-Zero%20C%20Runtime%20%2F%20Zero%20Libc-orange.svg?style=for-the-badge)](#)
[![Version: 2.0.0](https://img.shields.io/badge/Version-v2.0.0-informational.svg?style=for-the-badge)](https://github.com/SouvikNandi2004/rawx-core-language)

**A high-performance, register-first systems language engineered from the silicon up.**  
Operates directly on native CPU registers, synthesizing standalone machine code binaries for **Windows**, **Linux**, **macOS**, and **Bare-Metal** with **Zero C Runtime**, **Zero Libc**, and **Zero External Linkers**.

[Repository](https://github.com/SouvikNandi2004/rawx-core-language) • [Features](#key-features) • [Installation](#quick-start) • [Instruction Set](#instruction-set-reference) • [Examples](#example-gallery) • [License](#license)

</div>

---

## 🌟 Key Features

- **Direct Hardware Register Control**:
  - Full access to all 16 AMD64 General Purpose Registers: `RAX`, `RCX`, `RDX`, `RBX`, `RSP`, `RBP`, `RSI`, `RDI`, `R8` through `R15`.
  - 32-bit (`EAX`..`R15D`), 16-bit (`AX`..`R15W`), and 8-bit (`AL`..`R15B`, `AH`..`BH`) sub-registers.
  - 128-bit SIMD Vector Registers: `XMM0` through `XMM15` for 4-lane parallel vector math.
- **Universal Multi-Target Binary Emitters**:
  - 🪟 **Windows (PE32+)**: Synthesizes standalone `.exe` executables with DOS/PE headers and `KERNEL32.DLL` Import Address Tables (IAT).
  - 🐧 **Linux (ELF64)**: Emits standalone 64-bit `ET_EXEC` ELF binaries using direct Linux kernel syscalls with **zero libc**.
  - 🍏 **macOS (Mach-O 64)**: Emits standalone 64-bit `MH_EXECUTE` Mach-O binaries using direct Darwin BSD syscalls.
  - 💻 **Bare-Metal (Flat Binary)**: Emits pure machine code (`.bin`) for operating system kernels, bootloaders, and firmware.
- **Hardware CPU Intrinsics & Atomics**:
  - Cycle-accurate CPU benchmarking via `rdtsc` and `rdtscp`.
  - Hardware discovery via `cpuid`.
  - Spin-loop power optimizations via `pause`.
  - Atomic synchronization via `lock cmpxchg` and `xchg`.
- **Branchless Computing**:
  - Condition flags evaluation via `test`.
  - Conditional set (`setcc`: `sete`, `setne`, `setl`, `setle`, `setg`, `setge`, `setb`, `seta`).
  - Conditional move (`cmovcc`: `cmove`, `cmovne`, `cmovl`, `cmovg`, etc.) to eliminate CPU branch mispredictions.
- **Modular Preprocessor Engine**:
  - Modular library inclusion via `%include "library.rx"`.
  - Text constants via `%define NAME VALUE` and `NAME equ VALUE`.
  - Cross-platform conditional compilation via `%ifdef`, `%ifndef`, `%else`, `%endif`.
- **Lightning Compilation Speed**:
  - Compiles programs into native standalone executables in under 35 milliseconds.

---

## 🏗️ Architecture & Toolchain Pipeline

```
                             RawX Source File (.rx)
                                       │
                                       ▼
                 Modular Preprocessor Engine (%include, %define, %ifdef)
                                       │
                                       ▼
                         Lexer & AST Parser (GPRs, SIB, SIMD)
                                       │
                                       ▼
                   Two-Pass AMD64 Machine Code Instruction Encoder
             ├── REX Prefix Resolution (W, R, X, B)
             ├── ModR/M & SIB Byte Encoding
             ├── 16 GPRs + 16 Vector Registers (XMM0..XMM15)
             ├── Atomics, Intrinsics & Branchless Computing
             └── Symbol Table & Relocation Resolution
                                       │
         ┌─────────────────────────────┼─────────────────────────────┐
         ▼                             ▼                             ▼
  Windows Emitter                Linux Emitter                 macOS Emitter
   (PE32+ .exe)                   (ELF64 .elf)              (Mach-O 64 .macho)
Win64 ABI & IAT Imports     Zero-libc Linux Syscalls       Zero-libc Darwin Syscalls
```

---

## 🚀 Quick Start

### 1. Clone the Repository
```bash
git clone https://github.com/SouvikNandi2004/rawx-core-language.git
cd rawx-core-language
```

### 2. Verify Compiler
```powershell
.\rxc.exe -v
.\rxc.exe --info
```

### 3. Compile & Run Any Program
```powershell
# Compile and run immediately on Windows (-r)
.\rxc.exe examples\02_hello.rx -r

# Compile 64-bit Fibonacci sequence generator
.\rxc.exe examples\03_fibonacci.rx -r

# Cross-compile for Linux (produces bin\08_linux_sys_hello.elf)
.\rxc.exe examples\08_linux_sys_hello.rx -t linux

# Cross-compile for macOS (produces bin\09_macos_sys_hello.macho)
.\rxc.exe examples\09_macos_sys_hello.rx -t macos

# Compile universal cross-platform code
.\rxc.exe examples\10_cross_platform_unified.rx -t windows -r
.\rxc.exe examples\10_cross_platform_unified.rx -t linux
.\rxc.exe examples\10_cross_platform_unified.rx -t macos

# Inspect emitted hex machine code disassembly (-d)
.\rxc.exe examples\11_hardware_intrinsics.rx -d

# Run 128-bit SIMD vector engine on XMM0..XMM15
.\rxc.exe examples\12_simd_vector.rx -r
```

---

## 🌐 Universal Cross-Platform Execution Matrix

| Target Platform | Binary Format | ABI & Calling Convention | System Invocation |
| :--- | :---: | :--- | :--- |
| **Windows** | PE32+ (`.exe`) | Win64 ABI (`RCX`, `RDX`, `R8`, `R9`, 32B shadow stack) | Direct `KERNEL32.DLL` IAT Imports |
| **Linux** | ELF64 (`.elf`) | System V AMD64 (`RAX`, `RDI`, `RSI`, `RDX`, `R10`, `R8`) | `syscall` (`0F 05`) with **zero libc** |
| **macOS** | Mach-O 64 (`.macho`) | Darwin BSD Syscall (`RAX=0x2000000+num`, `RDI`, `RSI`, `RDX`) | `syscall` (`0F 05`) with **zero libc** |
| **Bare-Metal** | Flat Binary (`.bin`) | Ring 0 / Real / Long Mode direct machine code | Bootloaders, OS kernels, firmware |

---

## 📖 Instruction Set Reference

### Data Movement & Memory
- `mov dst, src`: Register-to-register, immediate-to-register, memory loads and stores.
- `lea dst, [mem]`: Load effective address (hardware pointer arithmetic).
- `push reg / imm`: Push 64-bit value to hardware stack.
- `pop reg`: Pop 64-bit value from top of stack.
- `xchg reg1, reg2` / `xchg reg, [mem]`: Atomic register/memory swap.
- `cmpxchg [mem], reg` / `lock cmpxchg`: Atomic compare and exchange.

### Arithmetic & Logic
- `add dst, src` / `sub dst, src`: Integer addition and subtraction.
- `imul dst, src`: Signed multiplication (`reg, reg` or `reg, imm`).
- `idiv reg`: 128-bit / 64-bit signed divide (`RDX:RAX` by operand).
- `inc reg` / `dec reg`: Increment / decrement.
- `neg reg` / `not reg`: Two's complement negation / bitwise NOT.
- `cqo` / `cdq`: Sign extension (`RAX` into `RDX:RAX` or `EAX` into `EDX:EAX`).
- `xor dst, src` / `and dst, src` / `or dst, src`: Bitwise logic.
- `test reg, reg` / `test reg, imm`: Update CPU flags without modifying destination.
- `shl reg, count` / `shr reg, count` / `sar reg, count`: Bit shifts (immediate or `cl`).

### Branchless Computing
- `setcc reg8`: Sets an 8-bit register (`al`, `bl`, `cl`, `dl`, `r8b`..`r15b`) to 1 or 0 based on condition flags:  
  `sete`/`setz`, `setne`/`setnz`, `setl`, `setle`, `setg`, `setge`, `setb`, `setbe`, `seta`, `setae`.
- `cmovcc reg, reg/mem`: Conditional move:  
  `cmove`/`cmovz`, `cmovne`/`cmovnz`, `cmovl`, `cmovle`, `cmovg`, `cmovge`, `cmovb`, `cmovbe`, `cmova`, `cmovae`.

### SIMD & 128-bit Vector Math (SSE / AVX)
- `movups xmm, xmm/[mem]` & `movups [mem], xmm`: 128-bit unaligned vector move.
- `movaps xmm, xmm/[mem]` & `movaps [mem], xmm`: 128-bit 16-byte aligned vector move.
- `xorps xmm, xmm`: Zero vector register or bitwise XOR.
- `addps xmm, xmm`: Parallel 4x 32-bit floating point addition.
- `subps xmm, xmm`: Parallel 4x 32-bit floating point subtraction.
- `mulps xmm, xmm`: Parallel 4x 32-bit floating point multiplication.
- `divps xmm, xmm`: Parallel 4x 32-bit floating point division.
- `pxor xmm, xmm`, `movdqa`, `movdqu`: Integer vector instructions.

### Hardware CPU Intrinsics
- `syscall`: Direct kernel entry (Linux & macOS).
- `rdtsc`: Read cycle-accurate CPU time-stamp counter into `EDX:EAX`.
- `rdtscp`: Read CPU time-stamp counter and core ID.
- `cpuid`: Query CPU vendor, model, and hardware features.
- `pause`: Low-power spin-wait hint.
- `int3`: Hardware software breakpoint for debuggers.
- `leave`: Fast stack frame teardown.

---

## 🧩 Preprocessor & Modularity

RawX includes a built-in preprocessor:

```rx
// Include external modules and reusable math libraries
%include "math_lib.rx"

// Define constants
%define BUFFER_SIZE 4096
SUCCESS_CODE equ 0

// Target-conditional cross-platform compilation
%ifdef TARGET_LINUX
    // Linux-specific direct kernel syscalls
%elif TARGET_MACOS
    // macOS-specific BSD syscalls
%else
    // Windows Win64 fallback
%endif
```

Automatically defined platform symbols:
- `TARGET_WINDOWS`, `__WINDOWS__`, `_WIN64`, `OS_WINDOWS`
- `TARGET_LINUX`, `__LINUX__`, `_LINUX64`, `OS_LINUX`
- `TARGET_MACOS`, `__MACOS__`, `__DARWIN__`, `_MACOS64`, `OS_MACOS`
- `RAWX`, `ARCH_AMD64`, `RAWX_VERSION_2`

---

## 📁 Repository Organization

```
rawx-core-language/
├── bin/                             <-- ALL compiled executables (.exe, .elf, .macho, .bin)
│   ├── 01_registers.exe
│   ├── 02_hello.exe
│   ├── 03_fibonacci.exe
│   ├── 04_memory_io.exe
│   ├── 05_benchmark.exe
│   ├── 08_linux_sys_hello.elf       <-- Linux standalone 64-bit ELF binary
│   ├── 09_macos_sys_hello.macho     <-- macOS standalone 64-bit Mach-O binary
│   ├── 10_cross_platform_unified.exe
│   ├── 11_hardware_intrinsics.exe   <-- RDTSC, CPUID, CMOVcc, SETcc
│   ├── 12_simd_vector.exe           <-- 128-bit XMM0..XMM15 Vector Engine
│   ├── 13_modular_math.exe          <-- Multi-file %include library architecture
│   └── rxc.exe                      <-- Standalone compiler executable
├── examples/                        <-- Pure .rx source code files
│   ├── 01_registers.rx              <-- 16 AMD64 GPRs manipulation
│   ├── 02_hello.rx                  <-- Windows console output
│   ├── 03_fibonacci.rx              <-- 64-bit Fibonacci sequence
│   ├── 04_memory_io.rx              <-- SIB memory addressing & string manipulation
│   ├── 05_benchmark.rx              <-- 500,000,000 register ops benchmark
│   ├── 06_native_emitter.rx         <-- Self-synthesizing binary emitter
│   ├── 07_brand_new_logic.rx        <-- Custom math & dynamic logic
│   ├── 08_linux_sys_hello.rx        <-- Pure Linux zero-libc ELF64 syscalls
│   ├── 09_macos_sys_hello.rx        <-- Pure macOS zero-libc Mach-O syscalls
│   ├── 10_cross_platform_unified.rx <-- Universal multi-target code
│   ├── 11_hardware_intrinsics.rx    <-- RDTSC, PAUSE, XCHG, TEST, CMOV, SETcc
│   ├── 12_simd_vector.rx            <-- SIMD 128-bit XMM vector processing
│   ├── 13_modular_math.rx           <-- Modular library driver
│   └── math_lib.rx                  <-- Reusable math library
├── src/                             <-- Compiler source code (modular & updatable)
│   ├── AST.cs                       <-- Tokens, AST nodes & relocation structures
│   ├── Lexer.cs                     <-- Lexical analyzer & tokenizer
│   ├── Preprocessor.cs              <-- %include, %define, %ifdef engine
│   ├── Parser.cs                    <-- AST parser & register table
│   ├── Encoder.cs                   <-- AMD64 machine code encoder
│   ├── PEBuilder.cs                 <-- Windows PE32+ emitter
│   ├── ELFBuilder.cs                <-- Linux ELF64 emitter
│   ├── MachOBuilder.cs              <-- macOS Mach-O 64 emitter
│   ├── FlatBinBuilder.cs            <-- Raw bare-metal binary emitter
│   ├── Disassembler.cs              <-- Machine code disassembler
│   └── Program.cs                   <-- CLI frontend
├── compiler.rx                      <-- Self-hosting compiler core
├── rxc.rx                           <-- Native RawX implementation of compiler
├── LICENSE                          <-- MIT License
├── README.md                        <-- Language documentation & reference
└── rxc.exe                          <-- Native Universal Multi-Target Compiler
```

---

## 🛠️ How to Rebuild & Update the Compiler

RawX is built with a modular, self-contained architecture so it can be updated and recompiled instantly:

```powershell
# Rebuild rxc.exe instantly using Windows' built-in csc.exe compiler
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:anycpu /out:rxc.exe src\*.cs
Copy-Item rxc.exe bin\rxc.exe -Force
```

On Linux or macOS with `.NET` or Mono:
```bash
csc -target:exe -out:rxc.exe src/*.cs
```

---

## 📜 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

Developed with passion by **[Souvik Nandi](https://github.com/SouvikNandi2004)**.  
⭐ If you find RawX exciting, consider starring the repository on [GitHub](https://github.com/SouvikNandi2004/rawx-core-language)!
