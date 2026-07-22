import { writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { apiBaseUrl } from './environment.mjs';

const output = resolve(import.meta.dirname, '../runtime-config.js');
const payload = `window.__ZUMBO_RUNTIME_CONFIG__ = Object.freeze(${JSON.stringify({ apiBaseUrl })});\n`;
writeFileSync(output, payload, 'utf8');
console.log(`Generated ${output} from ZUMBO_API_URL.`);
