import { createHash } from 'node:crypto';
import { access, readFile, readdir, stat, writeFile } from 'node:fs/promises';
import { dirname, relative, resolve, sep } from 'node:path';
import { pathToFileURL } from 'node:url';
import { createSecurityHeaders, verifyStrictCspCompatibility } from './build-frontend.mjs';
import { apiBaseUrl } from './environment.mjs';

const root = resolve(import.meta.dirname, '..');
const distRoot = resolve(root, 'dist-modern');
const surfaces = Object.freeze([
  Object.freeze({ name: 'desktop', directory: 'modern-desktop', scope: '/modern-desktop/' }),
  Object.freeze({ name: 'mobile', directory: 'modern-mobile', scope: '/modern-mobile/' })
]);

export async function hardenModernFrontend() {
  const runtimeConfig = `window.__ZUMBO_RUNTIME_CONFIG__ = Object.freeze(${JSON.stringify({ apiBaseUrl })});\n`;
  for (const surface of surfaces) {
    const directory = resolve(distRoot, surface.directory);
    await access(resolve(directory, 'index.html'));
    await writeFile(resolve(directory, 'runtime-config.js'), runtimeConfig, 'utf8');
    await verifySurface(directory, surface);
  }

  const baseHeaders = createSecurityHeaders(apiBaseUrl);
  const securityHeaders = {
    ...baseHeaders,
    'Content-Security-Policy': baseHeaders['Content-Security-Policy']
      .replace("base-uri 'none'", "base-uri 'self'")
      .replace("style-src 'self'", "style-src 'self' 'nonce-__ZUMBO_CSP_NONCE__'")
      .concat("; trusted-types angular angular#bundler zumbo#service-worker default; require-trusted-types-for 'script'")
  };
  await writeFile(resolve(distRoot, 'security-headers.json'), `${JSON.stringify(securityHeaders, null, 2)}\n`, 'utf8');
  await verifyStrictCspCompatibility(distRoot, securityHeaders['Content-Security-Policy']);
  await verifyNoExternalRuntimeReferences();

  const lockfile = await readFile(resolve(root, 'pnpm-lock.yaml'));
  const assets = [];
  for (const path of (await listFiles(distRoot)).filter(path => path !== 'asset-manifest.json')) {
    const body = await readFile(resolve(distRoot, path));
    assets.push({ path, bytes: body.byteLength, sha256: sha256(body) });
  }
  const manifest = {
    schemaVersion: 1,
    generatedFromLock: sha256(lockfile),
    runtimeApiOrigin: new URL(apiBaseUrl).origin,
    surfaces: surfaces.map(surface => ({
      name: surface.name,
      directory: surface.directory,
      scope: surface.scope,
      serviceWorker: `${surface.directory}/ngsw-worker.js`
    })),
    assets
  };
  await writeFile(resolve(distRoot, 'asset-manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');
  await verifyModernAssetManifest();
  return manifest;
}

export async function verifyModernAssetManifest() {
  const manifest = JSON.parse(await readFile(resolve(distRoot, 'asset-manifest.json'), 'utf8'));
  const actual = (await listFiles(distRoot)).filter(path => path !== 'asset-manifest.json');
  if (JSON.stringify(actual) !== JSON.stringify(manifest.assets.map(asset => asset.path))) {
    throw new Error('Modern asset manifest file list does not match dist-modern.');
  }
  for (const asset of manifest.assets) {
    const body = await readFile(resolve(distRoot, asset.path));
    if (body.byteLength !== asset.bytes || sha256(body) !== asset.sha256) {
      throw new Error(`Modern asset integrity verification failed: ${asset.path}`);
    }
  }
  return manifest;
}

async function verifySurface(directory, surface) {
  const index = await readFile(resolve(directory, 'index.html'), 'utf8');
  if (!index.includes(`<base href="${surface.scope}">`)) {
    throw new Error(`${surface.name} base href does not match its isolated scope.`);
  }
  if (!/ngcspnonce="__ZUMBO_CSP_NONCE__"/i.test(index)) {
    throw new Error(`${surface.name} shell does not expose the per-response Angular CSP nonce.`);
  }
  if (!/<meta\s+name="csp-nonce"\s+content="__ZUMBO_CSP_NONCE__">/i.test(index)) {
    throw new Error(`${surface.name} shell does not expose the per-response web-component CSP nonce.`);
  }
  if (/<style\b|\sstyle\s*=|\sonload\s*=/i.test(index)) {
    throw new Error(`${surface.name} shell contains inline style execution incompatible with strict CSP.`);
  }
  const webManifest = JSON.parse(await readFile(resolve(directory, 'manifest.webmanifest'), 'utf8'));
  if (webManifest.scope !== surface.scope || /[?&](?:fresh|cache|v)=/i.test(webManifest.start_url || '')) {
    throw new Error(`${surface.name} web manifest scope or canonical start URL is invalid.`);
  }
  const worker = JSON.parse(await readFile(resolve(directory, 'ngsw.json'), 'utf8'));
  if (worker.configVersion !== 1 || !worker.index.startsWith(surface.scope)) {
    throw new Error(`${surface.name} Angular service worker does not own the expected scope.`);
  }
  const cachedAssets = worker.assetGroups.flatMap(group => group.urls ?? []);
  if (cachedAssets.length === 0 || !cachedAssets.some(path => /\.js$/.test(path))) {
    throw new Error(`${surface.name} Angular service worker has an empty application shell.`);
  }
  const hashedAssets = Object.keys(worker.hashTable ?? {});
  if (hashedAssets.some(path => path.endsWith('/runtime-config.js')) || hashedAssets.some(path => path.endsWith('/index.html'))) {
    throw new Error(`${surface.name} dynamic shell inputs must not be versioned as application assets.`);
  }
  const shellPatterns = worker.assetGroups.flatMap(group => group.patterns ?? []).join('\n');
  const scopedIndexPattern = surface.scope.replaceAll('/', '\\/') + 'index\\.html';
  if (!shellPatterns.includes(scopedIndexPattern)) {
    throw new Error(`${surface.name} nonce-bearing index is not covered by the unversioned shell cache.`);
  }
  const runtimeGroup = worker.dataGroups.find(group => group.name === `${surface.name}-runtime-config`);
  if (!runtimeGroup || runtimeGroup.strategy !== 'freshness' || runtimeGroup.maxSize !== 1 || runtimeGroup.timeoutMs !== 2000) {
    throw new Error(`${surface.name} runtime config does not use the bounded network-first cache contract.`);
  }
  const dataPatterns = worker.dataGroups.flatMap(group => group.patterns ?? []).join('\n');
  if (/\\\/api(?:\\\/|$)|\\\/hubs(?:\\\/|$)|browser-auth/i.test(dataPatterns)) {
    throw new Error(`${surface.name} service worker must not cache API, hub or browser-auth data.`);
  }
  for (const reference of index.matchAll(/<(?:script|link)\b[^>]*(?:src|href)=["']([^"']+)["'][^>]*>/gi)) {
    const value = reference[1];
    if (/^(?:https?:)?\/\//i.test(value) || /[?#]/.test(value)) {
      throw new Error(`${surface.name} index contains an external or cache-busted asset reference: ${value}`);
    }
    const localPath = resolve(directory, value);
    await access(localPath).catch(() => {
      throw new Error(`${surface.name} index references a missing local asset: ${value}`);
    });
  }
}

async function verifyNoExternalRuntimeReferences() {
  const forbiddenHost = /(?:cdn\.jsdelivr\.net|cdnjs\.cloudflare\.com|unpkg\.com|fonts\.googleapis\.com|fonts\.gstatic\.com)/i;
  for (const path of await listFiles(distRoot)) {
    if (!/\.(?:html|css|js)$/i.test(path) || path.endsWith('runtime-config.js')) continue;
    const body = await readFile(resolve(distRoot, path), 'utf8');
    if (forbiddenHost.test(body)) throw new Error(`Modern runtime contains a CDN reference: ${path}`);
    if (/\.(?:html|css)$/i.test(path) && /(?:src|href|@import|url\()\s*(?:=\s*)?["']?(?:https?:)?\/\//i.test(body)) {
      throw new Error(`Modern document or stylesheet contains an external runtime reference: ${path}`);
    }
  }
}

async function listFiles(directory) {
  const files = [];
  async function visit(current) {
    for (const entry of await readdir(current, { withFileTypes: true })) {
      const path = resolve(current, entry.name);
      if (entry.isDirectory()) await visit(path);
      else if (entry.isFile() && (await stat(path)).isFile()) {
        files.push(relative(directory, path).split(sep).join('/'));
      }
    }
  }
  await visit(directory);
  return files.sort();
}

function sha256(value) {
  return createHash('sha256').update(value).digest('hex');
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const manifest = await hardenModernFrontend();
  console.log(`${manifest.assets.length} modern assets generated and verified with SHA-256.`);
}
