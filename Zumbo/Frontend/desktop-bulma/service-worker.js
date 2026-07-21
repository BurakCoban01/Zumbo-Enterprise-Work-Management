const CACHE_NAME = '__ZUMBO_GENERATED_CACHE_NAME__';
const CACHE_PREFIX = 'zumbo-desktop-shell-';
const MANIFEST_URL = './pwa-manifest.json';
const NAVIGATION_FALLBACK = './index.html';

self.addEventListener('install', event => {
  event.waitUntil(installVerifiedShell());
});

self.addEventListener('message', event => {
  if (event.data && event.data.type === 'SKIP_WAITING') self.skipWaiting();
});

self.addEventListener('activate', event => {
  event.waitUntil((async () => {
    const keys = await caches.keys();
    await Promise.all(keys
      .filter(key => key.startsWith(CACHE_PREFIX) && key !== CACHE_NAME)
      .map(key => caches.delete(key)));
    await self.clients.claim();
  })());
});

self.addEventListener('fetch', event => {
  if (event.request.method !== 'GET') return;
  const url = new URL(event.request.url);
  if (isApiOrHub(url) || url.origin !== self.location.origin) return;

  if (event.request.mode === 'navigate') {
    event.respondWith(fetch(event.request).catch(async () => {
      const cache = await caches.open(CACHE_NAME);
      const fallbackUrl = new URL(NAVIGATION_FALLBACK, self.location.href);
      const fallback = await cache.match(fallbackUrl);
      if (!fallback) throw new Error('Offline navigation shell is unavailable.');
      const requestUrl = new URL(event.request.url);
      const isFallbackRequest = requestUrl.origin === fallbackUrl.origin
        && requestUrl.pathname === fallbackUrl.pathname;
      return isFallbackRequest
        ? fallback
        : self.Response.redirect(fallbackUrl.href, 302);
    }));
    return;
  }

  event.respondWith(caches.open(CACHE_NAME).then(async cache =>
    (await cache.match(event.request)) || fetch(event.request)));
});

async function installVerifiedShell() {
  const stagingName = CACHE_NAME + '-installing';
  await caches.delete(stagingName);
  try {
    const response = await fetch(MANIFEST_URL, { cache: 'no-store', credentials: 'same-origin' });
    if (!response.ok) throw new Error('PWA manifest request failed: ' + response.status);
    const manifest = await response.json();
    validateManifest(manifest);

    const staging = await caches.open(stagingName);
    for (const asset of manifest.assets) {
      const request = new self.Request(new URL(asset.url, self.location.href), {
        cache: 'reload',
        credentials: 'same-origin'
      });
      const assetResponse = await fetch(request);
      if (!assetResponse.ok || assetResponse.type === 'opaque') {
        throw new Error('PWA asset request failed: ' + asset.url);
      }
      const bytes = await assetResponse.clone().arrayBuffer();
      if (bytes.byteLength !== asset.bytes || await sha256(bytes) !== asset.sha256) {
        throw new Error('PWA asset integrity failed: ' + asset.url);
      }
      await staging.put(request, assetResponse);
    }

    const target = await caches.open(CACHE_NAME);
    for (const request of await staging.keys()) {
      await target.put(request, await staging.match(request));
    }
  } finally {
    await caches.delete(stagingName);
  }
}

function validateManifest(manifest) {
  if (manifest?.schemaVersion !== 1
    || manifest.cacheName !== CACHE_NAME
    || manifest.scope !== './'
    || manifest.navigationFallback !== NAVIGATION_FALLBACK
    || !Array.isArray(manifest.assets)
    || manifest.assets.length === 0) {
    throw new Error('PWA manifest contract is invalid.');
  }
  for (const asset of manifest.assets) {
    const url = new URL(asset.url, self.location.href);
    if (url.origin !== self.location.origin
      || isApiOrHub(url)
      || !Number.isSafeInteger(asset.bytes)
      || asset.bytes < 0
      || !/^[a-f0-9]{64}$/.test(asset.sha256)) {
      throw new Error('PWA manifest asset is invalid: ' + asset.url);
    }
  }
  if (!manifest.assets.some(asset => asset.url === NAVIGATION_FALLBACK)) {
    throw new Error('PWA navigation fallback is not part of the verified shell.');
  }
}

function isApiOrHub(url) {
  return url.pathname.startsWith('/api/') || url.pathname.startsWith('/hubs/');
}

async function sha256(bytes) {
  const digest = await self.crypto.subtle.digest('SHA-256', bytes);
  return Array.from(new Uint8Array(digest), value => value.toString(16).padStart(2, '0')).join('');
}
