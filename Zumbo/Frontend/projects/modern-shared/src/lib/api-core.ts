export interface ApiEnvelope<T> {
  readonly success: boolean;
  readonly data: T;
  readonly error?: { readonly code?: string; readonly message?: string } | null;
  readonly correlationId?: string | null;
}

export interface ZumboApiError {
  readonly isZumboApiError: true;
  readonly status: number;
  readonly code: string;
  readonly message: string;
  readonly correlationId: string | null;
  readonly retryable: boolean;
  readonly canceled: boolean;
  readonly stale: boolean;
  readonly data: {
    readonly error: { readonly code: string; readonly message: string };
    readonly correlationId: string | null;
  };
}

export function resolveBaseUrl(value: string | null | undefined, fallback: string): string {
  return (value?.trim() || fallback).replace(/\/+$/, '');
}

export function isSafeMethod(method: string): boolean {
  return ['GET', 'HEAD', 'OPTIONS'].includes(method.toUpperCase());
}

export function canReplay(method: string, idempotencyKey: string | null): boolean {
  return isSafeMethod(method) || !!idempotencyKey;
}

export function validateIdempotencyKey(value: string | null | undefined): string | null {
  if (value == null || value === '') return null;
  const normalized = value.trim();
  if (!normalized || normalized.length > 128 || /[\r\n]/.test(normalized)) {
    throw syntheticApiError('IDEMPOTENCY_KEY_INVALID', 'The idempotency key must contain between 1 and 128 safe characters.', 400);
  }
  return normalized;
}

export function createIdentifier(prefix: string): string {
  if (typeof crypto.randomUUID === 'function') return `${prefix}${crypto.randomUUID()}`;
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  return prefix + [...bytes].map(value => value.toString(16).padStart(2, '0')).join('');
}

export function unwrapEnvelope<T>(body: ApiEnvelope<T> | T): T {
  if (body && typeof body === 'object' && 'success' in body && 'data' in body) {
    return (body as ApiEnvelope<T>).data;
  }
  return body as T;
}

export function readCookie(cookieHeader: string, name: string): string | null {
  const prefix = `${encodeURIComponent(name)}=`;
  const entry = cookieHeader.split(';').map(value => value.trim()).find(value => value.startsWith(prefix));
  if (!entry) return null;
  try {
    return decodeURIComponent(entry.slice(prefix.length)) || null;
  } catch {
    return null;
  }
}

export function syntheticApiError(
  code: string,
  message: string,
  status = 0,
  correlationId: string | null = null,
  flags: Partial<Pick<ZumboApiError, 'retryable' | 'canceled' | 'stale'>> = {}
): ZumboApiError {
  return {
    isZumboApiError: true,
    status,
    code,
    message,
    correlationId,
    retryable: flags.retryable ?? false,
    canceled: flags.canceled ?? false,
    stale: flags.stale ?? false,
    data: { error: { code, message }, correlationId }
  };
}

export function normalizeApiError(error: unknown, fallbackCorrelationId: string | null = null): ZumboApiError {
  if (isZumboApiError(error)) return error;
  const candidate = (error ?? {}) as {
    status?: number;
    error?: ApiEnvelope<unknown> | { error?: { code?: string; message?: string }; correlationId?: string };
  };
  const status = Number.isFinite(Number(candidate.status)) ? Number(candidate.status) : 0;
  const envelope = candidate.error && typeof candidate.error === 'object' ? candidate.error : null;
  const apiError = envelope && 'error' in envelope ? envelope.error : null;
  const correlationId = envelope && 'correlationId' in envelope && typeof envelope.correlationId === 'string'
    ? envelope.correlationId
    : fallbackCorrelationId;
  const canceled = status === 0 && error instanceof DOMException && error.name === 'AbortError';
  const code = apiError?.code || (canceled ? 'REQUEST_CANCELLED'
    : status === 0 ? 'NETWORK_UNAVAILABLE'
      : status === 401 ? 'AUTHENTICATION_REQUIRED'
        : status === 403 ? 'FORBIDDEN'
          : status === 404 ? 'NOT_FOUND'
            : status === 409 ? 'CONFLICT'
              : status === 429 ? 'RATE_LIMITED'
                : status >= 500 ? 'SERVER_UNAVAILABLE' : 'REQUEST_FAILED');
  const message = apiError?.message?.slice(0, 500) || (canceled ? 'The request was canceled.'
    : status === 0 ? 'The service could not be reached.'
      : status === 401 ? 'Authentication is required.'
        : status === 403 ? 'This action is not permitted.'
          : status === 404 ? 'The requested resource was not found.'
            : status === 429 ? 'Too many requests were sent.'
              : status >= 500 ? 'The service is temporarily unavailable.' : 'The request could not be completed.');
  return syntheticApiError(code, message, status, correlationId, {
    retryable: status === 0 || status === 408 || status === 425 || status === 429 || status >= 500,
    canceled
  });
}

export function resourceIdentity(url: string): { kind: string; id: string } | null {
  const sprintItem = url.match(/^\/api\/sprints\/[^/?]+\/items\/([^/?]+)/);
  if (sprintItem) return { kind: 'work-items', id: sprintItem[1] };
  const collaboration = url.match(/^\/api\/work-items\/([^/?]+)\/(?:collaboration|watch|vote|activity)(?:[/?]|$)/);
  if (collaboration) return { kind: 'work-item-collaboration', id: collaboration[1] };
  const match = url.match(/^\/api\/(teams|projects|boards|work-items|workflows|automations|dashboards|portfolios|goals|capacity-plans|knowledge-documents)\/([^/?]+)/);
  return match ? { kind: match[1], id: match[2] } : null;
}

function isZumboApiError(value: unknown): value is ZumboApiError {
  return !!value && typeof value === 'object' && (value as ZumboApiError).isZumboApiError === true;
}
