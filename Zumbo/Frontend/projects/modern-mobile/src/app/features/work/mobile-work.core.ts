import type { MobileWorkItemRecord } from '../../shell/mobile-workspace.models';

export function mergeUniqueWorkItems(
  current: readonly MobileWorkItemRecord[],
  next: readonly MobileWorkItemRecord[]
): readonly MobileWorkItemRecord[] {
  const ids = new Set(current.map(item => item.id));
  return [...current, ...next.filter(item => !ids.has(item.id))];
}
