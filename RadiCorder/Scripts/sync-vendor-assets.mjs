import { copyFileSync, existsSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';

const mappings = [
    {
        source: resolve('node_modules/hls.js/dist/hls.min.js'),
        destination: resolve('wwwroot/lib/hls.js/hls.min.js')
    },
    {
        source: resolve('node_modules/@microsoft/signalr/dist/browser/signalr.min.js'),
        destination: resolve('wwwroot/lib/signalr/signalr.min.js')
    }
];

for (const entry of mappings) {
    if (!existsSync(entry.source)) {
        console.warn(`[sync:vendors] skipped (missing): ${entry.source}`);
        continue;
    }

    mkdirSync(dirname(entry.destination), { recursive: true });
    copyFileSync(entry.source, entry.destination);
    console.log(`[sync:vendors] copied: ${entry.destination}`);
}
