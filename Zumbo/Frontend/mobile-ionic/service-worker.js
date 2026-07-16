const CACHE_NAME = 'zumbo-mobile-shell-v11';
const APP_SHELL = [
  './',
  './index.html',
  './styles.css',
  './app.js',
  './manifest.webmanifest',
  '../shared/zumbo-mark.svg',
  '../shared/zumbo-mark-192.png',
  '../shared/zumbo-mark-512.png'
];
const STATIC_HOSTS = new Set(['code.ionicframework.com', 'cdn.jsdelivr.net']);

self.addEventListener('install', event => {
  event.waitUntil(caches.open(CACHE_NAME).then(cache => cache.addAll(APP_SHELL)));
  self.skipWaiting();
});

self.addEventListener('activate', event => {
  event.waitUntil(caches.keys().then(keys => Promise.all(
    keys.filter(key => key.startsWith('zumbo-mobile-shell-') && key !== CACHE_NAME).map(key => caches.delete(key))
  )));
  self.clients.claim();
});

self.addEventListener('fetch', event => {
  if (event.request.method !== 'GET') return;
  const url = new URL(event.request.url);
  const isApi = url.port === '5088' || url.pathname.startsWith('/api/') || url.pathname.startsWith('/hubs/');
  if (isApi) return;

  if (url.origin === self.location.origin) {
    event.respondWith(caches.match(event.request).then(cached => cached || fetch(event.request).then(response => {
      const copy = response.clone();
      caches.open(CACHE_NAME).then(cache => cache.put(event.request, copy));
      return response;
    }).catch(() => caches.match('./index.html'))));
    return;
  }

  if (STATIC_HOSTS.has(url.hostname)) {
    event.respondWith(caches.match(event.request).then(cached => cached || fetch(event.request).then(response => {
      const copy = response.clone();
      caches.open(CACHE_NAME).then(cache => cache.put(event.request, copy));
      return response;
    })));
  }
});
