// ============================================================================
// RawX: Bare-Metal Systems Programming Language for Node.js (ES Module)
// 100% Pure JavaScript | Zero Dependencies | Browser & Node.js Universal
// https://github.com/SouvikNandi2004/rawx-core-language
// ============================================================================

import { createRequire } from 'module';
const require = createRequire(import.meta.url);
const rawxCommonJS = require('./rawx.js');

export const RawX = rawxCommonJS.RawX;
export const Lexer = rawxCommonJS.Lexer;
export const Parser = rawxCommonJS.Parser;
export const Encoder = rawxCommonJS.Encoder;
export const Preprocessor = rawxCommonJS.Preprocessor;

export default RawX;
