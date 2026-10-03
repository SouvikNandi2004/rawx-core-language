#!/usr/bin/env node
// ============================================================================
// RawX Universal Remote GitHub Loader
// Load & run RawX dynamically from GitHub Raw links with ZERO local dependencies!
// https://github.com/SouvikNandi2004/rawx-core-language
// ============================================================================

const fs = require('fs');
const path = require('path');

const RAWX_GITHUB_RAW_URL = 'https://raw.githubusercontent.com/SouvikNandi2004/rawx-core-language/main/rawx.js';

/**
 * Dynamically loads the RawX compiler engine directly from GitHub raw link.
 * Falls back to local cached copy if offline.
 * 
 * @param {string} [rawUrl] Optional custom raw URL or branch
 * @returns {Promise<typeof import('./rawx')>} The RawX module
 */
async function loadRawX(rawUrl = RAWX_GITHUB_RAW_URL) {
    const cacheDir = path.join(process.cwd(), '.rawx_cache');
    if (!fs.existsSync(cacheDir)) fs.mkdirSync(cacheDir, { recursive: true });
    const cacheFile = path.join(cacheDir, 'rawx_engine.js');

    let code = '';
    let fetched = false;

    if (typeof fetch !== 'undefined') {
        try {
            const resp = await fetch(rawUrl);
            if (resp.ok) {
                code = await resp.text();
                fs.writeFileSync(cacheFile, code, 'utf8');
                fetched = true;
            }
        } catch (e) {
            // Network failure: fallback to cache
        }
    }

    if (!fetched) {
        if (fs.existsSync(cacheFile)) {
            code = fs.readFileSync(cacheFile, 'utf8');
        } else if (fs.existsSync(path.join(__dirname, 'rawx.js'))) {
            code = fs.readFileSync(path.join(__dirname, 'rawx.js'), 'utf8');
        } else {
            throw new Error(`Failed to load RawX from ${rawUrl} and no offline cache available.`);
        }
    }

    // Strip shebang line if present
    code = code.replace(/^#![^\r\n]*/, '');

    const mod = { exports: {} };
    const runner = new Function('module', 'exports', 'require', '__dirname', '__filename', code);
    runner(mod, mod.exports, require, cacheDir, cacheFile);

    return mod.exports;
}

// CLI usage: node rawx-loader.js <file.rx | raw_url> [options]
if (require.main === module) {
    (async () => {
        const args = process.argv.slice(2);
        if (args.length === 0 || args.includes('-h') || args.includes('--help')) {
            console.log('========================================================');
            console.log(' [RawX Dynamic Remote Loader] Direct GitHub Raw Runner');
            console.log('========================================================');
            console.log(' Usage: node rawx-loader.js <source.rx | URL> [options]');
            console.log('   -o <path>          Output binary path');
            console.log('   -t, --target <os>  Target OS: windows, linux, macos');
            console.log('   -f, --format <fmt> Format: pe, elf, macho, bin');
            console.log('   -r, --run          Immediately execute compiled binary');
            console.log('========================================================');
            process.exit(0);
        }

        const { RawX } = await loadRawX();

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

        const binPath = await RawX.compileFile(src, { target, format, outPath: out });
        console.log(`[+] Standalone binary synthesized: ${binPath}`);

        if (doRun) {
            console.log(`\n[Running ${binPath}...]\n--------------------------------------------------------`);
            const res = await RawX.run(binPath, { stdio: 'inherit' });
            console.log(`--------------------------------------------------------\n[Exit Code: ${res.exitCode}]`);
        }
    })().catch(err => {
        console.error('[Loader Error]', err.message);
        process.exit(1);
    });
}

module.exports = { loadRawX, RAWX_GITHUB_RAW_URL };
