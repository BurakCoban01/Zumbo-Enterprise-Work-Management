import type { MobileWorkItemRecord } from '../../shell/mobile-workspace.models';

export type ProjectWorkFocus = 'total' | 'active' | 'done' | 'overdue';

export function mergeUniqueWorkItems(
  current: readonly MobileWorkItemRecord[],
  next: readonly MobileWorkItemRecord[]
): readonly MobileWorkItemRecord[] {
  const ids = new Set(current.map(item => item.id));
  return [...current, ...next.filter(item => !ids.has(item.id))];
}

export function filterProjectWorkItems(
  items: readonly MobileWorkItemRecord[],
  focus: ProjectWorkFocus | null,
  firstStatus?: string,
  now = Date.now()
): readonly MobileWorkItemRecord[] {
  if (!focus || focus === 'total') return items;
  if (focus === 'active') return items.filter(item => !item.completedAt && item.status !== firstStatus);
  if (focus === 'done') return items.filter(item => !!item.completedAt);
  return items.filter(item => !item.completedAt && !!item.dueDate && new Date(item.dueDate).getTime() < now);
}
