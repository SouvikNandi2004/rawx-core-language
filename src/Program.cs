using System;
using System.Diagnostics;
using System.IO;

namespace RawX
{
    public class Program
    {
        public const string VERSION = "2.0.0 (Universal Multi-Target Release)";

        public static int Main(string[] args)
        {
            if (args.Length == 0 ||
                args[0] == "-h" || args[0] == "--help" || args[0] == "help" || args[0] == "/?")
            {
                ShowHelp();
                return 0;
            }

            if (args[0] == "-v" || args[0] == "--version" || args[0] == "version")
            {
                ShowVersion();
                return 0;
            }

            if (args[0] == "--info")
            {
                ShowInfo();
                return 0;
            }

            if (args[0] == "--update" || args[0] == "--check-update")
            {
                CheckUpdate();
                return 0;
            }

            string srcPath = null;
            string outPath = null;
            TargetPlatform target = TargetPlatform.Windows;
            BinaryFormat format = BinaryFormat.PE;
            bool formatSpecified = false;
            bool doRun = false;
            bool doDisasm = false;

            // Detect host OS default
            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                target = TargetPlatform.Linux;
                format = BinaryFormat.ELF;
            }

            // Parse CLI Arguments
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "-o" && i + 1 < args.Length)
                {
                    outPath = args[++i];
                }
                else if ((arg == "-t" || arg == "--target") && i + 1 < args.Length)
                {
                    string tStr = args[++i].ToLowerInvariant();
                    if (tStr == "windows" || tStr == "win" || tStr == "pe")
                    {
                        target = TargetPlatform.Windows;
                        if (!formatSpecified) format = BinaryFormat.PE;
                    }
                    else if (tStr == "linux" || tStr == "elf")
                    {
                        target = TargetPlatform.Linux;
                        if (!formatSpecified) format = BinaryFormat.ELF;
                    }
                    else if (tStr == "macos" || tStr == "mac" || tStr == "darwin" || tStr == "macho")
                    {
                        target = TargetPlatform.MacOS;
                        if (!formatSpecified) format = BinaryFormat.MachO;
                    }
                    else
                    {
                        Console.WriteLine(" [Error] Unknown target: '" + tStr + "'. Supported: windows, linux, macos");
                        return 1;
                    }
                }
                else if ((arg == "-f" || arg == "--format") && i + 1 < args.Length)
                {
                    string fStr = args[++i].ToLowerInvariant();
                    formatSpecified = true;
                    if (fStr == "pe" || fStr == "exe") format = BinaryFormat.PE;
                    else if (fStr == "elf") format = BinaryFormat.ELF;
                    else if (fStr == "macho") format = BinaryFormat.MachO;
                    else if (fStr == "bin" || fStr == "raw") format = BinaryFormat.FlatBin;
                    else
                    {
                        Console.WriteLine(" [Error] Unknown format: '" + fStr + "'. Supported: pe, elf, macho, bin");
                        return 1;
                    }
                }
                else if (arg == "-r" || arg == "--run")
                {
                    doRun = true;
                }
                else if (arg == "-d" || arg == "--disasm")
                {
                    doDisasm = true;
                }
                else if (!arg.StartsWith("-"))
                {
                    if (srcPath == null)
                    {
                        srcPath = arg;
                    }
                }
            }

            if (string.IsNullOrEmpty(srcPath))
            {
                ShowHelp();
                return 1;
            }

            string actualSourceFile = srcPath;
            if (srcPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                srcPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(" [*] Fetching remote RawX source from URL...");
                Console.WriteLine("     URL: " + srcPath);
                try
                {
                    string cacheDir = Path.Combine(Directory.GetCurrentDirectory(), ".rawx_cache");
                    if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
                    string uriPath = new Uri(srcPath).AbsolutePath;
                    string urlFile = Path.GetFileName(uriPath);
                    if (string.IsNullOrEmpty(urlFile) || !urlFile.EndsWith(".rx"))
                    {
                        urlFile = "remote_" + Math.Abs(srcPath.GetHashCode()) + ".rx";
                    }
                    string localCached = Path.Combine(cacheDir, urlFile);
                    using (var client = new System.Net.WebClient())
                    {
                        client.DownloadFile(srcPath, localCached);
                    }
                    actualSourceFile = localCached;
                    Console.WriteLine(" [+] Successfully cached remote source -> " + localCached);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(" [Error] Failed to fetch remote source URL: " + ex.Message);
                    return 1;
                }
            }
            else if (!File.Exists(srcPath))
            {
                Console.WriteLine("========================================================");
                Console.WriteLine(" [RawX Native Compiler - RXC] 100% Pure Bare-Metal");
                Console.WriteLine(" Architecture: Universal AMD64 | Windows - Linux - macOS");
                Console.WriteLine("========================================================");
                Console.WriteLine(" [Error] Cannot open source file: '" + srcPath + "'");
                Console.WriteLine(" [Hint] File does not exist or is a directory.");
                Console.WriteLine(" [Hint] Please provide a valid .rx file.");
                Console.WriteLine("========================================================");
                return 1;
            }

            // Determine default output path
            if (string.IsNullOrEmpty(outPath))
            {
                string baseName = Path.GetFileNameWithoutExtension(actualSourceFile);
                string ext = ".exe";
                if (format == BinaryFormat.ELF) ext = ".elf";
                else if (format == BinaryFormat.MachO) ext = ".macho";
                else if (format == BinaryFormat.FlatBin) ext = ".bin";

                outPath = Path.Combine("bin", baseName + ext);
            }

            // Banner
            Console.WriteLine("========================================================");
            Console.WriteLine(" [RawX Native Compiler - RXC] 100% Pure Bare-Metal");
            Console.WriteLine(" Architecture: Universal AMD64 | Multi-OS Native Toolchain");
            Console.WriteLine(" Target OS   : " + target + " (Format: " + format + ")");
            Console.WriteLine("========================================================");
            Console.WriteLine(" [*] Compiling '" + srcPath + "' -> '" + outPath + "'...");

            try
            {
                Stopwatch sw = Stopwatch.StartNew();

                // 1. Read & Preprocess
                string rawSource = File.ReadAllText(actualSourceFile);
                Preprocessor prep = new Preprocessor(actualSourceFile, target);
                string preprocessed = prep.Process(rawSource, Path.GetFullPath(actualSourceFile));

                // 2. Lexical Analysis
                Lexer lexer = new Lexer(preprocessed);
                var tokens = lexer.Tokenize();

                // 3. Syntactic Analysis (AST)
                Parser parser = new Parser(tokens);
                ProgramAST ast = parser.Parse();

                // 4. Machine Code Generation (AMD64)
                Encoder encoder = new Encoder(target);
                EncodedProgram encoded = encoder.Encode(ast);

                // 5. Binary Emission
                switch (format)
                {
                    case BinaryFormat.PE:
                        PEBuilder.Build(encoded, outPath);
                        break;
                    case BinaryFormat.ELF:
                        ELFBuilder.Build(encoded, outPath);
                        break;
                    case BinaryFormat.MachO:
                        MachOBuilder.Build(encoded, outPath);
                        break;
                    case BinaryFormat.FlatBin:
                        FlatBinBuilder.Build(encoded, outPath);
                        break;
                }

                sw.Stop();

                Console.WriteLine(" [+] Build SUCCESS: Standalone native binary generated in " + sw.ElapsedMilliseconds + "ms!");
                Console.WriteLine(" [+] Output: " + Path.GetFullPath(outPath));
                Console.WriteLine(" [+] Machine code size: " + encoded.TextBytes.Count + " bytes | Data: " + encoded.DataBytes.Count + " bytes");
                Console.WriteLine("========================================================");

                // Disassembly
                if (doDisasm)
                {
                    Console.WriteLine(Disassembler.Dump(encoded));
                }

                // Run immediately if requested
                if (doRun)
                {
                    if (target == TargetPlatform.Windows && format == BinaryFormat.PE)
                    {
                        Console.WriteLine("\n[Running " + outPath + "...]");
                        Console.WriteLine("--------------------------------------------------------");
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = Path.GetFullPath(outPath),
                            UseShellExecute = false,
                            RedirectStandardOutput = false,
                            RedirectStandardError = false
                        };
                        using (Process p = Process.Start(psi))
                        {
                            p.WaitForExit();
                            Console.WriteLine("--------------------------------------------------------");
                            Console.WriteLine("[Process exited with code " + p.ExitCode + " (0x" + p.ExitCode.ToString("X") + ")]");
                        }
                    }
                    else
                    {
                        Console.WriteLine("\n[Note] Target is " + target + " (" + format + "). Run binary on native host or emulator (e.g. WSL / QEMU).");
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("========================================================");
                Console.WriteLine(" [Error] Compilation failed: " + ex.Message);
                Console.WriteLine("========================================================");
                return 1;
            }
        }

        private static void ShowHelp()
        {
            Console.WriteLine("========================================================");
            Console.WriteLine(" [RawX Native Compiler - RXC] 100% Pure Bare-Metal");
            Console.WriteLine(" Version " + VERSION);
            Console.WriteLine(" Architecture: Universal AMD64 / x86-64 | Windows - Linux - macOS");
            Console.WriteLine("========================================================");
            Console.WriteLine(" Usage: rxc <source.rx> [options]");
            Console.WriteLine();
            Console.WriteLine(" Options:");
            Console.WriteLine("   -o <path>               Specify output executable file path");
            Console.WriteLine("   -t, --target <os>       Target OS: windows, linux, macos (default: host)");
            Console.WriteLine("   -f, --format <fmt>      Output format: pe, elf, macho, bin");
            Console.WriteLine("   -r, --run               Immediately run binary after compilation");
            Console.WriteLine("   -d, --disasm            Show disassembly & emitted hex opcodes");
            Console.WriteLine("   -v, --version           Display compiler version and platform info");
            Console.WriteLine("   --info                  Display full CPU register & instruction matrix");
            Console.WriteLine("   --update                Check toolchain status & self-update info");
            Console.WriteLine("   -h, --help              Display this help message");
            Console.WriteLine();
            Console.WriteLine(" Examples:");
            Console.WriteLine("   rxc examples\\01_registers.rx");
            Console.WriteLine("   rxc examples\\02_hello.rx -r");
            Console.WriteLine("   rxc examples\\08_linux_sys_hello.rx -t linux");
            Console.WriteLine("   rxc examples\\09_macos_sys_hello.rx -t macos");
            Console.WriteLine("   rxc kernel.rx -f bin -o bin\\kernel.bin");
            Console.WriteLine("========================================================");
        }

        private static void ShowVersion()
        {
            Console.WriteLine("========================================================");
            Console.WriteLine(" RawX Systems Programming Language (RXC)");
            Console.WriteLine(" Version: " + VERSION);
            Console.WriteLine(" Architecture: AMD64 / x86-64 (All 16 GPRs + XMM0..XMM15)");
            Console.WriteLine(" Platforms Supported:");
            Console.WriteLine("   - Windows  : PE32+ 64-bit native executables (.exe)");
            Console.WriteLine("   - Linux    : ELF64 standalone zero-libc executables (.elf)");
            Console.WriteLine("   - macOS    : Mach-O 64-bit executables (.macho)");
            Console.WriteLine("   - Bare-Metal: Flat Binary for OS kernels & firmware (.bin)");
            Console.WriteLine(" Runtime: 100% Zero Runtime | Direct Hardware Machine Code");
            Console.WriteLine("========================================================");
        }

        private static void ShowInfo()
        {
            Console.WriteLine("========================================================");
            Console.WriteLine(" [RawX Hardware Architectural Matrix]");
            Console.WriteLine("========================================================");
            Console.WriteLine(" 1. Registers:");
            Console.WriteLine("    64-bit : RAX, RCX, RDX, RBX, RSP, RBP, RSI, RDI, R8..R15");
            Console.WriteLine("    32-bit : EAX, ECX, EDX, EBX, ESP, EBP, ESI, EDI, R8D..R15D");
            Console.WriteLine("    16-bit : AX, CX, DX, BX, SP, BP, SI, DI, R8W..R15W");
            Console.WriteLine("    8-bit  : AL, CL, DL, BL, SPL, BPL, SIL, DIL, R8B..R15B, AH..BH");
            Console.WriteLine("    SIMD   : XMM0 through XMM15 (128-bit vector registers)");
            Console.WriteLine();
            Console.WriteLine(" 2. Instruction Categories:");
            Console.WriteLine("    Data     : mov, lea, push, pop, xchg, cmpxchg");
            Console.WriteLine("    Math     : add, sub, imul, idiv, inc, dec, neg, not, cqo, cdq");
            Console.WriteLine("    Logic    : xor, and, or, test, shl, shr, sar");
            Console.WriteLine("    Branching: jmp, je/jz, jne/jnz, jl, jle, jg, jge, jb, jbe, ja, jae");
            Console.WriteLine("    Functions: call, ret, leave");
            Console.WriteLine("    Condition: sete/setne/setl/setle/setg/setge/setb/seta, cmovcc");
            Console.WriteLine("    Hardware : syscall, rdtsc, rdtscp, cpuid, pause, int3, lock");
            Console.WriteLine("    SIMD/SSE : movups, movaps, xorps, addps, subps, mulps, divps,");
            Console.WriteLine("               pxor, movdqa, movdqu");
            Console.WriteLine("    Bit Scan : bsf, bsr, popcnt");
            Console.WriteLine();
            Console.WriteLine(" 3. Cross-Platform Syscall Calling Conventions:");
            Console.WriteLine("    Linux (x86-64) : RAX=sys_num, RDI=arg1, RSI=arg2, RDX=arg3, R10=arg4, R8=arg5, R9=arg6");
            Console.WriteLine("    macOS (BSD 64) : RAX=0x2000000+num, RDI=arg1, RSI=arg2, RDX=arg3, R10=arg4, R8=arg5");
            Console.WriteLine("    Windows (x64)  : RCX=arg1, RDX=arg2, R8=arg3, R9=arg4, shadow stack (32B)");
            Console.WriteLine("========================================================");
        }

        private static void CheckUpdate()
        {
            Console.WriteLine("========================================================");
            Console.WriteLine(" [RawX Toolchain & Updatability Status]");
            Console.WriteLine("========================================================");
            Console.WriteLine(" Status          : Active & Ready");
            Console.WriteLine(" Current Version : " + VERSION);
            Console.WriteLine(" Architecture    : Multi-Target Autonomous Emitter");
            Console.WriteLine(" Source Directory: src/");
            Console.WriteLine(" Self-Hosting    : Yes (compiler.rx & rxc.rx)");
            Console.WriteLine(" Updatability    : Modular source design allows instant recompilation");
            Console.WriteLine("                   via built-in compilers without external dependencies.");
            Console.WriteLine("========================================================");
        }
    }
}
