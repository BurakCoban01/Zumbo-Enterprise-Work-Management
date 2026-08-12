import { AccountSession } from './mobile-account.models';

export function normalizeMutedTypes(value: string): readonly string[] {
  const seen = new Set<string>();
  return value.split(',').map(item => item.trim()).filter(item => {
    const key = item.toLocaleLowerCase('tr-TR');
    if (!item || seen.has(key)) return false;
    seen.add(key); return true;
  });
}

export function isSessionActive(session: AccountSession, now = Date.now()): boolean {
  const expires = Date.parse(session.expiresAt);
  return !session.revokedAt && Number.isFinite(expires) && expires > now;
}

export function visibleSessions(sessions: readonly AccountSession[], expanded = false, now = Date.now()): readonly AccountSession[] {
  const sorted = [...sessions].sort((left, right) => Number(right.isCurrent) - Number(left.isCurrent) || Number(isSessionActive(right, now)) - Number(isSessionActive(left, now)) || Date.parse(right.lastSeenAt) - Date.parse(left.lastSeenAt));
  if (expanded) return sorted;
  let active = 0; let inactive = 0;
  return sorted.filter(item => item.isCurrent || (isSessionActive(item, now) ? ++active <= 4 : ++inactive <= 2));
}

export function boundedExpiryDays(value: number): number { return Math.min(365, Math.max(1, Math.trunc(Number(value) || 90))); }
