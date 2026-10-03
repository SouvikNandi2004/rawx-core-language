// ============================================================================
// RawX - Node.js Direct Integration Example
// Demonstrates in-memory AMD64 native synthesis, cross-compilation & execution
// https://github.com/SouvikNandi2004/rawx-core-language
// ============================================================================

const { RawX } = require('../rawx.js');
const fs = require('fs');
const path = require('path');

async function main() {
    console.log('========================================================');
    console.log(' [RawX] Node.js Native Integration Demo');
    console.log(` Version: ${RawX.version} | Pure JS Zero-Dependency Engine`);
    console.log('========================================================\n');

    // ------------------------------------------------------------------------
    // 1. Compile and execute an inline RawX assembly string directly
    // ------------------------------------------------------------------------
    console.log('[Step 1] Compiling and running inline RawX assembly in Node.js...');
    const inlineSource = `
        section .data
        msg: db ">>> Hello from dynamically compiled RawX AMD64 binary!", 10, 0

        section .text
        global _start
        _start:
            sub rsp, 40
            mov rcx, -11           ; STD_OUTPUT_HANDLE
            call GetStdHandle

            mov rcx, rax           ; hConsoleOutput
            lea rdx, [rel msg]     ; lpBuffer
            mov r8, 59             ; nNumberOfBytesToWrite
            lea r9, [rsp + 32]     ; lpNumberOfBytesWritten
            mov qword [rsp + 32], 0
            call WriteFile

            xor ecx, ecx           ; uExitCode = 0
            call ExitProcess
    `;

    const runResult = await RawX.run(inlineSource);
    console.log('Captured Output:\n' + runResult.stdout);
    console.log(`Execution Exit Code: ${runResult.exitCode}\n`);

    // ------------------------------------------------------------------------
    // 2. Cross-compile for Linux (ELF64) and macOS (Mach-O 64) inside Node.js
    // ------------------------------------------------------------------------
    console.log('[Step 2] Cross-compiling standalone Linux ELF64 binary...');
    const linuxSource = `
        section .data
        msg: db "Linux native syscall output", 10
        len: equ 27

        section .text
        global _start
        _start:
            mov rax, 1             ; sys_write
            mov rdi, 1             ; stdout
            lea rsi, [rel msg]
            mov rdx, len
            syscall

            mov rax, 60            ; sys_exit
            xor rdi, rdi           ; status 0
            syscall
    `;

    const elfBuffer = await RawX.compile(linuxSource, { target: 'linux', format: 'elf' });
    const elfPath = path.join(__dirname, '..', 'bin', 'node_demo_linux.elf');
    fs.writeFileSync(elfPath, elfBuffer);
    console.log(`[+] Linux ELF64 binary generated: ${elfPath} (${elfBuffer.length} bytes)\n`);

    // ------------------------------------------------------------------------
    // 3. Compile an existing .rx file
    // ------------------------------------------------------------------------
    console.log('[Step 3] Compiling examples/02_hello.rx to Windows PE32+...');
    const helloRx = path.join(__dirname, '02_hello.rx');
    const binPath = await RawX.compileFile(helloRx, { target: 'windows', format: 'pe' });
    console.log(`[+] Synthesized: ${binPath}`);

    const helloRun = await RawX.run(binPath);
    console.log('Output:\n' + helloRun.stdout);

    console.log('========================================================');
    console.log(' [SUCCESS] Node.js Native Integration complete!');
    console.log('========================================================');
}

main().catch(err => {
    console.error('Demo error:', err);
    process.exit(1);
});
