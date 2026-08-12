import { ProjectWorkItem, ProjectWorkflow } from '../../work-items/project-work-item.models';
import { ProjectSprint } from '../project-planning.models';
import {
  BuildPlanningModelInput,
  PlanningCalendarDay,
  PlanningCalendarEvent,
  PlanningModel,
  PlanningRoadmapRow,
  PlanningSegment,
  PlanningTimelineRow,
  PlanningWindow
} from './project-planning-view.models';

const DAY_MS = 86_400_000;

export function buildPlanningModel(input: BuildPlanningModelInput): PlanningModel {
  const tasks = input.tasks.filter(task => matches(task, input.filters));
  const metadata = statusMetadata(input.workflow);
  const sprintById = new Map(input.sprints.map(sprint => [sprint.id, sprint]));
  const mutableRows = tasks.map(task => timelineRow(task, sprintById.get(task.sprintId ?? '')));
  const rowById = new Map(mutableRows.map(row => [row.id, row]));
  const edges = dependencies(tasks);
  for (const edge of edges) {
    const from = rowById.get(edge.from);
    const to = rowById.get(edge.to);
    if (!from || !to) continue;
    from.blocks.push(to.id);
    to.blockedBy.push(from.id);
    edge.risk = !!from.endKey && !!to.startKey && from.endKey >= to.startKey;
    if (edge.risk) to.dependencyRisk = true;
  }

  const events: PlanningCalendarEvent[] = [];
  for (const row of mutableRows) {
    if (!row.dueKey) continue;
    events.push({ id: `task-due-${row.id}`, key: row.dueKey, kind: 'Bitiş', title: row.title, task: row.task, tone: row.dependencyRisk ? 'risk' : isDone(row.task, metadata) ? 'done' : 'task' });
  }
  for (const sprint of input.sprints) {
    const start = dateKey(sprint.startDate);
    const end = dateKey(sprint.endDate);
    if (start) events.push({ id: `sprint-start-${sprint.id}`, key: start, kind: 'Sprint başı', title: sprint.name, tone: 'sprint' });
    if (end) events.push({ id: `sprint-end-${sprint.id}`, key: end, kind: 'Sprint sonu', title: sprint.name, tone: 'sprint' });
  }
  for (const milestone of input.project.milestones ?? []) {
    const key = dateKey(milestone.dueAt, input.timeZone, false);
    if (key) events.push({ id: `milestone-${milestone.id}`, key, kind: 'Kilometre taşı', title: milestone.name, tone: 'milestone' });
  }
  for (const release of input.project.releases ?? []) {
    const key = dateKey(release.scheduledAt, input.timeZone, false);
    if (key) events.push({ id: `release-${release.id}`, key, kind: 'Sürüm', title: release.name, tone: 'release' });
  }
  events.sort((left, right) => left.key.localeCompare(right.key) || left.title.localeCompare(right.title, 'tr-TR'));

  const byDate = new Map<string, PlanningCalendarEvent[]>();
  for (const event of events) byDate.set(event.key, [...(byDate.get(event.key) ?? []), event]);
  const anchorKey = dateKey(input.anchorDate, input.timeZone, true);
  const window = windowSpec(anchorKey, input.zoom);
  const timelineRows = placeRows(mutableRows.filter(row => !!row.startKey), window);
  const roadmapRows = placeRows(roadmapEntries(input, tasks, metadata), window);
  const calendar = calendarDays(anchorKey, input.calendarMode).map(day => ({ ...day, events: byDate.get(day.key) ?? [] }));
  const segments = statusSegments(taskDistribution(tasks), input.workflow);
  const total = tasks.length;
  const done = tasks.filter(task => isDone(task, metadata)).length;
  const today = dateKey(input.today ?? new Date(), input.timeZone, true);
  const overdue = tasks.filter(task => { const due = dateKey(task.dueDate); return !!due && due < today && !isDone(task, metadata); }).length;
  const projectDone = segments.filter(segment => normalized(segment.category) === 'done').reduce((sum, segment) => sum + segment.count, 0);

  return {
    anchorKey,
    calendarDays: calendar,
    calendarEvents: events,
    timelineRows,
    unscheduledTasks: tasks.filter(task => !dateKey(task.dueDate)),
    dependencyRisks: edges.filter(edge => edge.risk),
    roadmapRows,
    window,
    totals: {
      tasks: total,
      done,
      overdue,
      progress: total ? Math.round(done / total * 100) : 0,
      projectTasks: total,
      projectDone,
      projectProgress: total ? Math.round(projectDone / total * 100) : 0,
      projectSegments: segments
    }
  };
}

export function dateKey(value?: string | Date | null, timeZone?: string, preserveDateOnly = true): string {
  if (!value) return '';
  if (typeof value === 'string' && preserveDateOnly && /^\d{4}-\d{2}-\d{2}/.test(value)) return value.slice(0, 10);
  const date = value instanceof Date ? value : new Date(value);
  if (!Number.isFinite(date.getTime())) return '';
  if (!timeZone) return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
  const parts = Object.fromEntries(new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(date).map(part => [part.type, part.value]));
  return `${parts['year']}-${parts['month']}-${parts['day']}`;
}

export function addDays(key: string, amount: number): string {
  const date = keyDate(key);
  if (!date) return '';
  date.setUTCDate(date.getUTCDate() + amount);
  return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`;
}

export function formatPlanningDate(key: string, options: Intl.DateTimeFormatOptions = {}): string {
  const date = keyDate(key);
  return date ? new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'short', year: 'numeric', timeZone: 'UTC', ...options }).format(date) : 'Tarih yok';
}

function timelineRow(task: ProjectWorkItem, sprint?: ProjectSprint): MutableTimelineRow {
  const dueKey = dateKey(task.dueDate);
  const sprintStart = dateKey(sprint?.startDate);
  const sprintEnd = dateKey(sprint?.endDate);
  let startKey = sprintStart || dueKey;
  const endKey = dueKey || sprintEnd || startKey;
  if (startKey && endKey && endKey < startKey) startKey = endKey;
  return {
    id: task.id, task, title: task.title, startKey, endKey, dueKey,
    source: sprint && dueKey ? 'Sprint başlangıcı → görev bitişi' : sprint ? 'Sprint aralığından türetildi' : 'Yalnız görev bitiş tarihi',
    milestone: !!startKey && startKey === endKey, blockedBy: [], blocks: [], dependencyRisk: false,
    inWindow: false, column: 1, span: 1
  };
}

function roadmapEntries(input: BuildPlanningModelInput, tasks: readonly ProjectWorkItem[], metadata: StatusMetadata): MutableRoadmapRow[] {
  const rows: MutableRoadmapRow[] = [];
  for (const milestone of input.project.milestones ?? []) {
    const key = dateKey(milestone.dueAt, input.timeZone, false);
    if (key) rows.push(roadmapRow(`milestone-${milestone.id}`, 'Kilometre taşı', milestone.name, milestone.status, key, key, milestone.status === 'Completed' ? 100 : 0, 'Kilometre taşı durumu'));
  }
  for (const release of input.project.releases ?? []) {
    const key = dateKey(release.scheduledAt, input.timeZone, false);
    if (key) rows.push(roadmapRow(`release-${release.id}`, 'Sürüm', release.name, release.status, key, key, release.status === 'Published' ? 100 : release.status === 'Approved' ? 70 : 20, 'Sürüm yaşam döngüsü'));
  }
  for (const sprint of input.sprints) {
    const scoped = tasks.filter(task => task.sprintId === sprint.id);
    const complete = scoped.filter(task => isDone(task, metadata)).length;
    rows.push({
      ...roadmapRow(`sprint-${sprint.id}`, 'Sprint', sprint.name, sprint.status, dateKey(sprint.startDate), dateKey(sprint.endDate), scoped.length ? Math.round(complete / scoped.length * 100) : 0, scoped.length ? `${complete}/${scoped.length} görev tamamlandı` : 'Sprint kapsamı boş'),
      milestone: false,
      segments: statusSegments(taskDistribution(scoped), input.workflow)
    });
  }
  return rows.sort((left, right) => left.startKey.localeCompare(right.startKey) || left.title.localeCompare(right.title, 'tr-TR'));
}

function roadmapRow(id: string, kind: string, title: string, status: string, startKey: string, endKey: string, progress: number, progressSource: string): MutableRoadmapRow {
  return { id, kind, title, status, startKey, endKey, milestone: startKey === endKey, segments: [], progress, progressSource, inWindow: false, column: 1, span: 1 };
}

function placeRows<T extends { startKey: string; endKey: string; inWindow: boolean; column: number; span: number }>(rows: T[], window: PlanningWindow): T[] {
  return rows.map(row => {
    const first = clamp(Math.floor(dayDiff(window.startKey, row.startKey) / window.bucketDays), 0, 11);
    const last = clamp(Math.floor(dayDiff(window.startKey, row.endKey) / window.bucketDays), first, 11);
    return { ...row, inWindow: row.endKey >= window.startKey && row.startKey <= window.endKey, column: first + 1, span: last - first + 1 };
  });
}

function calendarDays(anchor: string, mode: 'month' | 'week'): Omit<PlanningCalendarDay, 'events'>[] {
  const start = mode === 'week' ? startOfWeek(anchor) : startOfWeek(`${anchor.slice(0, 7)}-01`);
  const month = anchor.slice(0, 7);
  return Array.from({ length: mode === 'week' ? 7 : 42 }, (_, index) => {
    const key = addDays(start, index);
    return { key, inRange: mode === 'week' || key.startsWith(month), label: formatPlanningDate(key, { day: 'numeric', month: mode === 'week' ? 'short' : undefined, year: undefined }) };
  });
}

function windowSpec(anchor: string, zoom: 'week' | 'month' | 'quarter'): PlanningWindow {
  const bucketDays = zoom === 'week' ? 2 : zoom === 'quarter' ? 30 : 7;
  const startKey = zoom === 'quarter' ? `${anchor.slice(0, 7)}-01` : startOfWeek(anchor);
  const buckets = Array.from({ length: 12 }, (_, index) => {
    const start = addDays(startKey, index * bucketDays);
    return { index: index + 1, startKey: start, endKey: addDays(start, bucketDays - 1), label: formatPlanningDate(start, zoom === 'quarter' ? { month: 'short', year: '2-digit', day: undefined } : { day: '2-digit', month: 'short', year: undefined }) };
  });
  return { startKey, endKey: addDays(startKey, bucketDays * 12 - 1), bucketDays, buckets };
}

function dependencies(tasks: readonly ProjectWorkItem[]): MutableDependency[] {
  const ids = new Set(tasks.map(task => task.id));
  const seen = new Set<string>();
  const edges: MutableDependency[] = [];
  for (const task of tasks) for (const relation of task.relations ?? []) {
    const type = normalized(relation.relationType);
    const from = type === 'blocks' ? task.id : type === 'blockedby' || type === 'isblockedby' ? relation.relatedWorkItemId : '';
    const to = type === 'blocks' ? relation.relatedWorkItemId : type === 'blockedby' || type === 'isblockedby' ? task.id : '';
    const id = `${from}>${to}`;
    if (!from || !to || !ids.has(from) || !ids.has(to) || seen.has(id)) continue;
    seen.add(id); edges.push({ id, from, to, risk: false });
  }
  return edges;
}

function statusSegments(distribution: readonly { status: string; count: number }[], workflow: ProjectWorkflow): PlanningSegment[] {
  const metadata = statusMetadata(workflow);
  const total = distribution.reduce((sum, item) => sum + Math.max(0, item.count), 0);
  const raw = distribution.filter(item => item.count > 0).map((item, index) => {
    const meta = metadata[item.status] ?? { category: 'Custom', order: 10_000 + index };
    const exact = total ? item.count / total * 10_000 : 0;
    return { status: item.status, category: meta.category, tone: tone(meta.category), count: item.count, units: Math.floor(exact), remainder: exact - Math.floor(exact), order: meta.order };
  });
  let missing = 10_000 - raw.reduce((sum, item) => sum + item.units, 0);
  for (const item of [...raw].sort((a, b) => b.remainder - a.remainder || a.order - b.order)) if (missing-- > 0) item.units += 1;
  return raw.map(item => ({ status: item.status, category: item.category, tone: item.tone, count: item.count, percentage: item.units / 100, order: item.order })).sort((a, b) => a.order - b.order || a.status.localeCompare(b.status, 'tr-TR'));
}

function statusMetadata(workflow: ProjectWorkflow): StatusMetadata {
  return Object.fromEntries((workflow.statuses ?? []).map((status, index) => [status.name, { category: status.category ?? 'Custom', order: status.position ?? index }]));
}

function taskDistribution(tasks: readonly ProjectWorkItem[]): { status: string; count: number }[] {
  const counts = new Map<string, number>();
  for (const task of tasks) counts.set(task.status, (counts.get(task.status) ?? 0) + 1);
  return [...counts].map(([status, count]) => ({ status, count }));
}

function isDone(task: ProjectWorkItem, metadata: StatusMetadata): boolean { return !!task.completedAt || normalized(metadata[task.status]?.category) === 'done'; }
function matches(task: ProjectWorkItem, filters: BuildPlanningModelInput['filters']): boolean {
  if (filters.assignee && task.assigneeUserId !== filters.assignee) return false;
  if (filters.type && task.type !== filters.type) return false;
  return !filters.query || normalized(`${task.title} ${task.description ?? ''} ${task.status} ${task.priority} ${task.labels.join(' ')}`).includes(normalized(filters.query));
}
function startOfWeek(key: string): string { const date = keyDate(key); return date ? addDays(key, -((date.getUTCDay() + 6) % 7)) : ''; }
function dayDiff(left: string, right: string): number { const a = keyDate(left); const b = keyDate(right); return a && b ? Math.round((b.getTime() - a.getTime()) / DAY_MS) : 0; }
function keyDate(key: string): Date | null { const parts = key.split('-').map(Number); return parts.length === 3 && parts.every(Number.isFinite) ? new Date(Date.UTC(parts[0], parts[1] - 1, parts[2])) : null; }
function normalized(value?: string | null): string { return String(value ?? '').trim().toLocaleLowerCase('tr-TR'); }
function tone(category: string): string { return normalized(category).replace(/[^a-z0-9]+/g, '-') || 'custom'; }
function pad(value: number): string { return String(value).padStart(2, '0'); }
function clamp(value: number, minimum: number, maximum: number): number { return Math.max(minimum, Math.min(maximum, value)); }

type StatusMetadata = Readonly<Record<string, { readonly category: string; readonly order: number }>>;
type MutableTimelineRow = Omit<PlanningTimelineRow, 'blockedBy' | 'blocks'> & { blockedBy: string[]; blocks: string[]; dependencyRisk: boolean };
type MutableRoadmapRow = Omit<PlanningRoadmapRow, 'segments'> & { segments: PlanningSegment[] };
interface MutableDependency { readonly id: string; readonly from: string; readonly to: string; risk: boolean; }
