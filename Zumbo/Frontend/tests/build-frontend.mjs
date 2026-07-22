import { createHash } from 'node:crypto';
import { access, cp, mkdir, readFile, readdir, rm, stat, writeFile } from 'node:fs/promises';
import { dirname, relative, resolve, sep } from 'node:path';
import { pathToFileURL } from 'node:url';
import { apiBaseUrl } from './environment.mjs';

const root = resolve(import.meta.dirname, '..');
const vendorRoot = resolve(root, 'vendor');
const distRoot = resolve(root, 'dist');

export const vendorAssetCatalog = Object.freeze([
  asset('angular', '1.8.3', 'angular/angular.min.js', 'angular/angular.min.js'),
  asset('bulma', '1.0.2', 'bulma/css/bulma.min.css', 'bulma/bulma.min.css'),
  asset('@microsoft/signalr', '8.0.7', '@microsoft/signalr/dist/browser/signalr.min.js', 'signalr/signalr.min.js'),
  asset('lucide', '1.24.0', 'lucide/dist/umd/lucide.min.js', 'lucide/lucide.min.js'),
  asset('ionic-sdk', '1.3.2', 'ionic-sdk/release/js/ionic.bundle.min.js', 'ionic/ionic.bundle.min.js'),
  asset('ionic-sdk', '1.3.2', 'ionic-sdk/release/css/ionic.min.css', 'ionic/css/ionic.min.css'),
  ...['eot', 'svg', 'ttf', 'woff'].map(extension =>
    asset('ionic-sdk', '1.3.2', `ionic-sdk/release/fonts/ionicons.${extension}`, `ionic/fonts/ionicons.${extension}`))
]);

export const pwaSurfaceCatalog = Object.freeze([
  Object.freeze({
    name: 'desktop',
    directory: 'desktop-bulma',
    cachePrefix: 'zumbo-desktop-shell-',
    manifestPath: 'desktop-bulma/pwa-manifest.json',
    workerPath: 'desktop-bulma/service-worker.js',
    vendorPrefixes: ['vendor/angular/', 'vendor/bulma/', 'vendor/lucide/', 'vendor/signalr/']
  }),
  Object.freeze({
    name: 'mobile',
    directory: 'mobile-ionic',
    cachePrefix: 'zumbo-mobile-shell-',
    manifestPath: 'mobile-ionic/pwa-manifest.json',
    workerPath: 'mobile-ionic/service-worker.js',
    vendorPrefixes: ['vendor/ionic/', 'vendor/signalr/']
  })
]);

export async function buildFrontend() {
  await rm(vendorRoot, { recursive: true, force: true });
  await rm(distRoot, { recursive: true, force: true });
  await mkdir(vendorRoot, { recursive: true });

  for (const entry of vendorAssetCatalog) {
    const source = resolve(root, 'node_modules', entry.source);
    const destination = resolve(vendorRoot, entry.destination);
    await assertPackageVersion(entry);
    await mkdir(dirname(destination), { recursive: true });
    await cp(source, destination);
  }

  await mkdir(distRoot, { recursive: true });
  for (const directory of ['desktop-bulma', 'mobile-ionic', 'shared', 'vendor']) {
    await cp(resolve(root, directory), resolve(distRoot, directory), { recursive: true });
  }
  const runtimeConfig = `window.__ZUMBO_RUNTIME_CONFIG__ = Object.freeze(${JSON.stringify({ apiBaseUrl })});\n`;
  await writeFile(resolve(distRoot, 'runtime-config.js'), runtimeConfig, 'utf8');
  const securityHeaders = createSecurityHeaders(apiBaseUrl);
  await writeFile(resolve(distRoot, 'security-headers.json'), `${JSON.stringify(securityHeaders, null, 2)}\n`, 'utf8');
  const pwa = await generatePwaArtifacts(distRoot);

  const cdnReferences = await findRuntimeCdnReferences(distRoot);
  if (cdnReferences.length > 0) {
    throw new Error(`Üretim çıktısında çalışma zamanı CDN referansı bulundu:\n${cdnReferences.join('\n')}`);
  }
  await verifyLocalDocumentAssets(distRoot);
  await verifyStrictCspCompatibility(distRoot, securityHeaders['Content-Security-Policy']);

  const lockfile = await readFile(resolve(root, 'pnpm-lock.yaml'));
  const assets = [];
  for (const file of await listFiles(distRoot)) {
    if (file === 'asset-manifest.json') continue;
    const body = await readFile(resolve(distRoot, file));
    assets.push({ path: file, bytes: body.byteLength, sha256: sha256(body) });
  }
  const manifest = {
    schemaVersion: 1,
    generatedFromLock: sha256(lockfile),
    pwa,
    assets
  };
  await writeFile(resolve(distRoot, 'asset-manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');
  await verifyAssetManifest(distRoot);
  return manifest;
}

export function createSecurityHeaders(apiUrl) {
  const apiOrigin = new URL(apiUrl).origin;
  const websocketOrigin = apiOrigin.replace(/^http:/, 'ws:').replace(/^https:/, 'wss:');
  return Object.freeze({
    'Content-Security-Policy': [
      "default-src 'none'",
      "base-uri 'none'",
      `connect-src 'self' ${apiOrigin} ${websocketOrigin}`,
      "font-src 'self'",
      "form-action 'self'",
      "frame-ancestors 'none'",
      "img-src 'self' data: blob:",
      "manifest-src 'self'",
      "object-src 'none'",
      "script-src 'self'",
      "style-src 'self'",
      "worker-src 'self'"
    ].join('; '),
    'Cross-Origin-Opener-Policy': 'same-origin',
    'Cross-Origin-Resource-Policy': 'same-origin',
    'Permissions-Policy': 'camera=(), microphone=(), geolocation=()',
    'Referrer-Policy': 'no-referrer',
    'Strict-Transport-Security': 'max-age=31536000; includeSubDomains',
    'X-Content-Type-Options': 'nosniff',
    'X-Frame-Options': 'DENY'
  });
}

export async function verifyStrictCspCompatibility(directory, policy) {
  if (/unsafe-inline|unsafe-eval|\*/i.test(policy)) {
    throw new Error('CSP unsafe-inline, unsafe-eval veya wildcard iceremez.');
  }

  for (const file of (await listFiles(directory)).filter(path => path.endsWith('.html'))) {
    const body = await readFile(resolve(directory, file), 'utf8');
    for (const match of body.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)) {
      const attributes = match[1];
      if (/\bsrc\s*=/i.test(attributes) || /\btype\s*=\s*["']text\/ng-template["']/i.test(attributes)) continue;
      if (match[2].trim()) throw new Error(`${file} CSP ile engellenecek satir ici calistirilabilir script iceriyor.`);
    }
    if (/<style\b/i.test(body) || /\sstyle\s*=/i.test(body)) {
      throw new Error(`${file} CSP ile engellenecek satir ici stil iceriyor.`);
    }
  }
}

export async function findRuntimeCdnReferences(directory = root) {
  const findings = [];
  const files = directory === root
    ? (await Promise.all(['desktop-bulma', 'mobile-ionic', 'shared'].map(async source =>
      (await listFiles(resolve(root, source))).map(file => `${source}/${file}`)))).flat().sort()
    : await listFiles(directory);
  for (const file of files.filter(path => {
    if (/\.(?:html|css)$/i.test(path)) return true;
    return path.endsWith('.js') && !path.endsWith('runtime-config.js') && !path.startsWith('vendor/');
  })) {
    const body = await readFile(resolve(directory, file), 'utf8');
    const patterns = file.endsWith('.html')
      ? [/(?:src|href)\s*=\s*["'](?:https?:)?\/\/[^"']+/gi]
      : file.endsWith('.css')
        ? [/(?:@import\s+|url\(\s*["']?)(?:https?:)?\/\/[^)"';\s]+/gi]
        : [/['"](?:https?:)?\/\/[^'"]+['"]/gi];
    for (const pattern of patterns) {
      for (const match of body.matchAll(pattern)) findings.push(`${file}: ${match[0]}`);
    }
  }
  return findings.sort();
}

export async function verifyAssetManifest(directory = distRoot) {
  const manifestPath = resolve(directory, 'asset-manifest.json');
  const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
  const actualFiles = (await listFiles(directory)).filter(file => file !== 'asset-manifest.json');
  const declaredFiles = manifest.assets.map(entry => entry.path);
  if (JSON.stringify(actualFiles) !== JSON.stringify(declaredFiles)) {
    throw new Error('Varlık manifestindeki dosya listesi üretim çıktısıyla eşleşmiyor.');
  }
  for (const entry of manifest.assets) {
    const body = await readFile(resolve(directory, entry.path));
    if (body.byteLength !== entry.bytes || sha256(body) !== entry.sha256) {
      throw new Error(`Varlık bütünlüğü doğrulanamadı: ${entry.path}`);
    }
  }
  for (const surface of pwaSurfaceCatalog) {
    const verified = await verifyPwaManifest(directory, surface.name);
    const declared = manifest.pwa?.[surface.name];
    if (!declared
      || declared.manifest !== surface.manifestPath
      || declared.cacheName !== verified.cacheName
      || declared.assets !== verified.assets.length) {
      throw new Error(`Varlık manifestindeki ${surface.name} PWA metadata'sı geçersiz.`);
    }
  }
  return manifest;
}

export async function generatePwaArtifacts(directory = distRoot) {
  const generated = {};
  for (const surface of pwaSurfaceCatalog) {
    const manifest = await describePwaSurface(directory, surface);
    await writeFile(resolve(directory, surface.manifestPath), `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');

    const template = await readFile(resolve(root, surface.workerPath), 'utf8');
    if (!template.includes('__ZUMBO_GENERATED_CACHE_NAME__')) {
      throw new Error(`${surface.workerPath} generated cache marker'i içermiyor.`);
    }
    const worker = template.replaceAll('__ZUMBO_GENERATED_CACHE_NAME__', manifest.cacheName);
    await writeFile(resolve(directory, surface.workerPath), worker, 'utf8');
    generated[surface.name] = Object.freeze({
      manifest: surface.manifestPath,
      cacheName: manifest.cacheName,
      assets: manifest.assets.length
    });
  }
  return Object.freeze(generated);
}

export async function verifyPwaManifest(directory = distRoot, surfaceName) {
  const surface = pwaSurfaceCatalog.find(entry => entry.name === surfaceName);
  if (!surface) throw new Error(`Bilinmeyen PWA surface: ${surfaceName}`);
  const actual = JSON.parse(await readFile(resolve(directory, surface.manifestPath), 'utf8'));
  const expected = await describePwaSurface(directory, surface);
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    throw new Error(`${surface.name} PWA manifest bütünlüğü doğrulanamadı.`);
  }
  const worker = await readFile(resolve(directory, surface.workerPath), 'utf8');
  if (!worker.includes(`'${actual.cacheName}'`) || worker.includes('__ZUMBO_GENERATED_CACHE_NAME__')) {
    throw new Error(`${surface.name} service worker generated cache sürümüyle eşleşmiyor.`);
  }
  return actual;
}

async function describePwaSurface(directory, surface) {
  const files = (await listFiles(directory)).filter(path => isPwaShellAsset(path, surface));
  const assets = [];
  for (const path of files) {
    const body = await readFile(resolve(directory, path));
    const relativeUrl = path.startsWith(`${surface.directory}/`)
      ? `./${path.slice(surface.directory.length + 1)}`
      : `../${path}`;
    assets.push({ url: relativeUrl, bytes: body.byteLength, sha256: sha256(body) });
  }
  assets.sort((left, right) => left.url.localeCompare(right.url));
  const contract = {
    schemaVersion: 1,
    scope: './',
    navigationFallback: './index.html',
    assets
  };
  const version = sha256(Buffer.from(JSON.stringify(contract))).slice(0, 20);
  return { ...contract, cacheName: `${surface.cachePrefix}${version}` };
}

function isPwaShellAsset(path, surface) {
  if (path === 'runtime-config.js') return true;
  if (path.startsWith('shared/')) return true;
  if (surface.vendorPrefixes.some(prefix => path.startsWith(prefix))) return true;
  if (!path.startsWith(`${surface.directory}/`)) return false;
  return path !== surface.workerPath && path !== surface.manifestPath;
}

async function verifyLocalDocumentAssets(directory) {
  for (const file of (await listFiles(directory)).filter(path => path.endsWith('.html'))) {
    const body = await readFile(resolve(directory, file), 'utf8');
    for (const match of body.matchAll(/<(?:script|link)\b[^>]*(?:src|href)=["']([^"']+)["'][^>]*>/gi)) {
      const reference = match[1].split(/[?#]/, 1)[0];
      if (!reference || /^(?:https?:|data:|#)/i.test(reference)) continue;
      const destination = resolve(dirname(resolve(directory, file)), reference);
      await access(destination).catch(() => {
        throw new Error(`${file} bulunamayan yerel varlığa başvuruyor: ${reference}`);
      });
    }
  }
}

async function assertPackageVersion(entry) {
  const packageJson = JSON.parse(await readFile(resolve(root, 'node_modules', entry.package, 'package.json'), 'utf8'));
  if (packageJson.version !== entry.version) {
    throw new Error(`${entry.package} sürümü ${entry.version} olmalı; kurulu sürüm ${packageJson.version}.`);
  }
}

async function listFiles(directory) {
  const files = [];
  async function visit(current) {
    for (const entry of await readdir(current, { withFileTypes: true })) {
      const path = resolve(current, entry.name);
      if (entry.isDirectory()) await visit(path);
      else if (entry.isFile() && (await stat(path)).isFile()) files.push(relative(directory, path).split(sep).join('/'));
    }
  }
  await visit(directory);
  return files.sort();
}

function asset(packageName, version, source, destination) {
  return Object.freeze({ package: packageName, version, source, destination });
}

function sha256(value) {
  return createHash('sha256').update(value).digest('hex');
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const manifest = await buildFrontend();
  console.log(`${manifest.assets.length} yerel varlık üretildi ve SHA-256 ile doğrulandı.`);
}
