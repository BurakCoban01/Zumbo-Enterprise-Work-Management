import { createServer } from 'node:http';
import { randomBytes } from 'node:crypto';
import { readFile, stat } from 'node:fs/promises';
import { extname, resolve, sep } from 'node:path';
import { pathToFileURL } from 'node:url';

const contentTypes = {
  '.css': 'text/css; charset=utf-8',
  '.eot': 'application/vnd.ms-fontobject',
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
  '.ttf': 'font/ttf',
  '.webmanifest': 'application/manifest+json; charset=utf-8',
  '.woff': 'font/woff'
};

export async function startStaticServer(directory, { host = '127.0.0.1', port = 0 } = {}) {
  const root = resolve(directory);
  const securityHeaders = JSON.parse(await readFile(resolve(root, 'security-headers.json'), 'utf8'));
  const server = createServer(async (request, response) => {
    try {
      const url = new URL(request.url || '/', `http://${request.headers.host || host}`);
      const decodedPath = decodeURIComponent(url.pathname);
      let path = resolve(root, `.${decodedPath}`);
      if (path !== root && !path.startsWith(`${root}${sep}`)) {
        response.writeHead(403).end('Forbidden');
        return;
      }
      try {
        if ((await stat(path)).isDirectory()) path = resolve(path, 'index.html');
      } catch (error) {
        const modernSurface = decodedPath.match(/^\/(modern-(?:desktop|mobile))(?:\/.*)?$/)?.[1];
        if (!modernSurface || error?.code !== 'ENOENT') throw error;
        path = resolve(root, modernSurface, 'index.html');
      }
      let body = await readFile(path);
      const nonce = randomBytes(18).toString('base64url');
      const headers = Object.fromEntries(Object.entries(securityHeaders).map(([name, value]) => [
        name,
        String(value).replaceAll('__ZUMBO_CSP_NONCE__', nonce)
      ]));
      if (path.endsWith('.html') && body.includes(Buffer.from('__ZUMBO_CSP_NONCE__'))) {
        body = Buffer.from(body.toString('utf8').replaceAll('__ZUMBO_CSP_NONCE__', nonce));
      }
      response.writeHead(200, {
        ...headers,
        'Cache-Control': /(?:service-worker|ngsw-worker)\.js$/.test(path) ? 'no-cache' : 'no-store',
        'Content-Length': body.byteLength,
        'Content-Type': contentTypes[extname(path).toLowerCase()] || 'application/octet-stream',
        'X-Content-Type-Options': 'nosniff'
      });
      response.end(body);
    } catch (error) {
      const status = error?.code === 'ENOENT' ? 404 : 500;
      response.writeHead(status, { 'Content-Type': 'text/plain; charset=utf-8' }).end(status === 404 ? 'Not Found' : 'Server Error');
    }
  });
  await new Promise((accept, reject) => {
    server.once('error', reject);
    server.listen(port, host, accept);
  });
  const address = server.address();
  return {
    origin: `http://${host}:${address.port}`,
    close: () => new Promise((accept, reject) => server.close(error => error ? reject(error) : accept()))
  };
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const directory = resolve(import.meta.dirname, '..', process.argv[2] || 'dist');
  const port = Number.parseInt(process.env.ZUMBO_FRONTEND_PORT || '58177', 10);
  const running = await startStaticServer(directory, { port });
  console.log(`Frontend ${running.origin} adresinde ${directory} klasöründen sunuluyor.`);
}
