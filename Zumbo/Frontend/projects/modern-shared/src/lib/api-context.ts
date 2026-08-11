import { HttpContextToken } from '@angular/common/http';

export const ZUMBO_CORRELATION_ID = new HttpContextToken<string | null>(() => null);
export const ZUMBO_IDEMPOTENCY_KEY = new HttpContextToken<string | null>(() => null);
export const ZUMBO_IF_MATCH = new HttpContextToken<string | null>(() => null);
export const ZUMBO_SKIP_REFRESH = new HttpContextToken<boolean>(() => false);
