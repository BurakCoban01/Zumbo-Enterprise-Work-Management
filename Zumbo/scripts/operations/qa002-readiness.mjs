const readinessPaths = Object.freeze(['/health/live', '/health/ready']);

export class Qa002TransportError extends Error {
  constructor(diagnostic, cause) {
    super(formatTransportError(diagnostic), { cause });
    this.name = 'Qa002TransportError';
    this.diagnostic = Object.freeze({ ...diagnostic });
    Object.assign(this, diagnostic);
  }
}

export class Qa002ReadinessTimeoutError extends Error {
  constructor(stage, diagnostic, inventory) {
    super(formatReadinessTimeout(stage, diagnostic, inventory));
    this.name = 'Qa002ReadinessTimeoutError';
    this.stage = stage;
    this.diagnostic = diagnostic ? Object.freeze({ ...diagnostic }) : null;
    this.inventory = structuredClone(inventory);
    if (diagnostic) Object.assign(this, diagnostic);
  }
}

export function validateQa002ApiUrl(rawValue, {
  gatewayBindHost,
  gatewayPort,
  composeGateway
} = {}) {
  let url;
  try {
    url = new URL(rawValue);
  } catch {
    throw new Error('ZUMBO_API_URL must be a valid absolute URL.');
  }
  if (url.protocol !== 'http:') throw new Error('ZUMBO_API_URL must use the Compose HTTP scheme.');
  if (!['127.0.0.1', 'localhost'].includes(url.hostname)) throw new Error('ZUMBO_API_URL must use a loopback host.');
  if (url.username || url.password) throw new Error('ZUMBO_API_URL must not contain credentials.');
  if (url.pathname !== '/' || url.search || url.hash) throw new Error('ZUMBO_API_URL must contain only a normalized origin.');
  if (!url.port) throw new Error('ZUMBO_API_URL must contain the gateway published port.');

  const configuredPort = parsePort(gatewayPort, 'ZUMBO_GATEWAY_PORT');
  const publishedPort = parsePort(composeGateway?.published, 'Compose gateway published port');
  const expectedPort = publishedPort || configuredPort;
  if (!expectedPort) throw new Error('The expected gateway published port is unavailable.');
  if (configuredPort && publishedPort && configuredPort !== publishedPort) {
    throw new Error('ZUMBO_GATEWAY_PORT does not match the Compose gateway published port.');
  }
  if (Number(url.port) !== expectedPort) throw new Error('ZUMBO_API_URL does not match the gateway published port.');

  const publishedHost = composeGateway?.host_ip || gatewayBindHost;
  if (publishedHost !== '127.0.0.1' || (gatewayBindHost && gatewayBindHost !== '127.0.0.1')) {
    throw new Error('The gateway must publish only on 127.0.0.1.');
  }
  return Object.freeze({
    origin: url.origin,
    hostname: url.hostname,
    port: expectedPort,
    scheme: url.protocol.slice(0, -1)
  });
}

export function createApiRequest({
  origin,
  fetchImpl = globalThis.fetch,
  requestTimeoutMs = 30_000,
  setTimeoutImpl = setTimeout,
  clearTimeoutImpl = clearTimeout
}) {
  if (typeof fetchImpl !== 'function') throw new Error('A fetch implementation is required.');
  const normalizedOrigin = new URL(origin).origin;
  const request = async function apiRequest(path, {
    method = 'GET',
    token,
    body,
    allowError = false,
    allowText = false,
    timeoutMs = requestTimeoutMs
  } = {}) {
    const safeMethod = String(method).toUpperCase();
    const safePath = normalizeRequestPath(path);
    const controller = new AbortController();
    const boundedTimeoutMs = Math.min(requestTimeoutMs, Math.max(1, Number(timeoutMs) || requestTimeoutMs));
    const timer = setTimeoutImpl(() => controller.abort(), boundedTimeoutMs);
    let response;
    let text;
    try {
      try {
        response = await fetchImpl(`${normalizedOrigin}${safePath}`, {
          method: safeMethod,
          headers: {
            ...(token ? { Authorization: `Bearer ${token}` } : {}),
            ...(body ? { 'Content-Type': 'application/json' } : {})
          },
          body: body ? JSON.stringify(body) : undefined,
          signal: controller.signal
        });
        text = await response.text();
      } catch (cause) {
        throw new Qa002TransportError(transportDiagnostic(cause, {
          method: safeMethod,
          path: safePath,
          origin: normalizedOrigin
        }), cause);
      }
      const payload = allowText ? text : (text ? JSON.parse(text) : {});
      const ok = response.status >= 200 && response.status < 300;
      if (!ok && !allowError) throw new Error(`API ${safeMethod} ${safePath} failed with HTTP ${response.status}.`);
      return {
        status: response.status,
        payload,
        request: { method: safeMethod, path: safePath, origin: normalizedOrigin }
      };
    } finally {
      clearTimeoutImpl(timer);
    }
  };
  Object.defineProperty(request, 'origin', { value: normalizedOrigin });
  return request;
}

export async function verifyQa002Readiness({
  stage,
  expectedServices,
  getInventory,
  apiRequest,
  onInventory = () => {},
  now = Date.now,
  sleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds)),
  timeoutMs = 180_000,
  pollIntervalMs = 3_000
}) {
  if (!['first', 'resume'].includes(stage)) throw new Error('Readiness stage must be first or resume.');
  if (!Number.isFinite(timeoutMs) || timeoutMs <= 0 || timeoutMs > 180_000) throw new Error('Readiness timeout must be within 180 seconds.');
  if (!Number.isFinite(pollIntervalMs) || pollIntervalMs <= 0 || pollIntervalMs > timeoutMs) throw new Error('Readiness poll interval is invalid.');
  const deadline = now() + timeoutMs;
  let lastInventory = [];
  let lastFailure = null;

  while (now() < deadline) {
    lastInventory = await getInventory({ timeoutMs: Math.min(30_000, Math.max(1, deadline - now())) });
    onInventory(structuredClone(lastInventory));
    if (exactInventoryReady(lastInventory, expectedServices)) {
      let tourPassed = true;
      for (const path of readinessPaths) {
        const requestTimeRemaining = deadline - now();
        if (requestTimeRemaining <= 0) {
          tourPassed = false;
          break;
        }
        try {
          const response = await apiRequest(path, {
            method: 'GET',
            allowError: true,
            allowText: true,
            timeoutMs: requestTimeRemaining
          });
          if (response.status !== 200) {
            tourPassed = false;
            lastFailure = {
              kind: 'http', stage, method: 'GET', path,
              origin: response.request?.origin || apiRequest.origin,
              status: response.status
            };
          }
        } catch (error) {
          if (!(error instanceof Qa002TransportError)) throw error;
          tourPassed = false;
          lastFailure = { kind: 'transport', stage, ...error.diagnostic };
        }
      }
      if (tourPassed) return lastInventory;
    }

    const remaining = deadline - now();
    if (remaining <= 0) break;
    await sleep(Math.min(pollIntervalMs, remaining));
  }
  throw new Qa002ReadinessTimeoutError(stage, lastFailure, lastInventory);
}

export function exactInventoryReady(inventory, expectedServices) {
  if (!Array.isArray(inventory) || inventory.length !== expectedServices.length) return false;
  const expected = [...expectedServices].sort();
  const observed = inventory.map(item => item.service).sort();
  return JSON.stringify(observed) === JSON.stringify(expected)
    && new Set(observed).size === expected.length
    && inventory.every(item => item.ready === true);
}

function transportDiagnostic(error, request) {
  const chain = errorChain(error);
  return {
    ...request,
    code: safeCode(firstValue(chain, 'code') || (chain.some(item => item?.name === 'AbortError') ? 'ABORT_ERR' : 'TRANSPORT_ERROR')),
    errno: safeToken(firstValue(chain, 'errno')),
    syscall: safeToken(firstValue(chain, 'syscall')),
    address: safeLoopbackAddress(firstValue(chain, 'address')),
    port: safePort(firstValue(chain, 'port'))
  };
}

function errorChain(error) {
  const values = [];
  const seen = new Set();
  for (let current = error; current && typeof current === 'object' && !seen.has(current); current = current.cause) {
    seen.add(current);
    values.push(current);
  }
  return values;
}

function firstValue(chain, key) {
  return chain.find(item => item?.[key] !== undefined)?.[key];
}

function formatTransportError(diagnostic) {
  const endpoint = endpointSuffix(diagnostic);
  return `API ${diagnostic.method} ${diagnostic.path} transport error ${diagnostic.code} at ${diagnostic.origin}${endpoint}.`;
}

function formatReadinessTimeout(stage, diagnostic, inventory) {
  const prefix = `${stage} readiness timed out`;
  if (diagnostic?.kind === 'transport') {
    return `${prefix}; last transport error ${diagnostic.code} for ${diagnostic.method} ${diagnostic.path} at ${diagnostic.origin}${endpointSuffix(diagnostic)}.`;
  }
  if (diagnostic?.kind === 'http') {
    return `${prefix}; last HTTP status ${diagnostic.status} for ${diagnostic.method} ${diagnostic.path} at ${diagnostic.origin}.`;
  }
  const failed = inventory.filter(item => !item.ready)
    .map(item => `${item.service}:${item.state}/${item.health}/${item.exitCode}`);
  return `${prefix}; last service inventory not ready${failed.length ? `: ${failed.join(', ')}` : '.'}`;
}

function endpointSuffix(diagnostic) {
  const fields = [];
  if (diagnostic.errno !== undefined) fields.push(`errno ${diagnostic.errno}`);
  if (diagnostic.syscall) fields.push(`syscall ${diagnostic.syscall}`);
  if (diagnostic.address) fields.push(`address ${diagnostic.address}`);
  if (diagnostic.port) fields.push(`port ${diagnostic.port}`);
  return fields.length ? ` (${fields.join(', ')})` : '';
}

function normalizeRequestPath(path) {
  if (typeof path !== 'string' || !/^\/[A-Za-z0-9/_-]+$/.test(path)) throw new Error('API request path must be a root-relative safe path.');
  return path;
}

function parsePort(value, label) {
  if (value === undefined || value === null || value === '') return 0;
  const port = Number(value);
  if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error(`${label} is invalid.`);
  return port;
}

function safeCode(value) {
  const token = String(value || '').toUpperCase();
  return /^[A-Z0-9_-]{1,40}$/.test(token) ? token : 'TRANSPORT_ERROR';
}

function safeToken(value) {
  if (value === undefined || value === null) return undefined;
  const token = String(value);
  return /^-?[A-Za-z0-9_.-]{1,40}$/.test(token) ? token : undefined;
}

function safeLoopbackAddress(value) {
  const address = String(value || '');
  return ['127.0.0.1', '::1', 'localhost'].includes(address) ? address : undefined;
}

function safePort(value) {
  const port = Number(value);
  return Number.isInteger(port) && port > 0 && port <= 65535 ? port : undefined;
}
