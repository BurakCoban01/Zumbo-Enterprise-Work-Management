import { AccountSession } from './account-settings.models';

export function normalizeMutedTypes(value: string | readonly string[]): readonly string[] {
  const seen = new Set<string>();
  const values = Array.isArray(value) ? value : String(value || '').split(',');
  return values.map(item => String(item).trim()).filter(item => {
    const key = item.toLocaleLowerCase('en-US');
    if (!item || seen.has(key)) return false;
    seen.add(key); return true;
  });
}

export function isSessionActive(session: AccountSession, now = Date.now()): boolean {
  if (session.revokedAt || !session.expiresAt) return false;
  const expiresAt = Date.parse(session.expiresAt);
  return Number.isFinite(expiresAt) && expiresAt > now;
}

export function visibleSessions(sessions: readonly AccountSession[], now = Date.now(), inactiveLimit = 2, activeLimit = 4): readonly AccountSession[] {
  let inactive = 0; let active = 0;
  return [...sessions].sort((left, right) => {
    const activity = Number(isSessionActive(right, now)) - Number(isSessionActive(left, now));
    if (activity) return activity;
    if (left.isCurrent !== right.isCurrent) return Number(right.isCurrent) - Number(left.isCurrent);
    return Date.parse(right.lastSeenAt || '0') - Date.parse(left.lastSeenAt || '0');
  }).filter(session => {
    if (isSessionActive(session, now)) return session.isCurrent || ++active <= activeLimit;
    return ++inactive <= inactiveLimit;
  });
}

export function boundedExpiryDays(value: number): number { return Math.min(365, Math.max(1, Math.trunc(Number(value) || 90))); }
export function privacyProgress(job: { readonly progressPercent?: number } | null): number { return Math.min(100, Math.max(0, Number(job?.progressPercent) || 0)); }
