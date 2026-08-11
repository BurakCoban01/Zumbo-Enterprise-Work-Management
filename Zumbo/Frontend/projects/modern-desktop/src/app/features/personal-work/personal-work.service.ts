import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, catchError, forkJoin, map, of } from 'rxjs';
import { ProjectSummary } from '../../shell/desktop-shell.models';
import { PersonalWorkItem, PersonalWorkPage, WorkItemSearchResult } from './personal-work.models';

interface ProjectSearchResult {
  readonly project: ProjectSummary;
  readonly result: WorkItemSearchResult | null;
}

@Injectable()
export class PersonalWorkService {
  private readonly api = inject(ZumboApiClient);

  load(projects: readonly ProjectSummary[], userId: string, page = 1, pageSize = 50): Observable<PersonalWorkPage> {
    const searches = projects.map(project => this.api.post<WorkItemSearchResult>('/api/work-items/search', {
      projectId: project.id,
      assigneeUserId: userId,
      page,
      pageSize
    }).pipe(
      map(result => ({ project, result }) satisfies ProjectSearchResult),
      catchError(() => of({ project, result: null } satisfies ProjectSearchResult))
    ));

    return (searches.length ? forkJoin(searches) : of([] as readonly ProjectSearchResult[])).pipe(map(results => {
      if (projects.length && results.every(result => result.result === null)) throw new Error('Personal work unavailable.');
      return {
      tasks: results.flatMap(({ project, result }) => (result?.items ?? []).map(task => ({
        ...task,
        projectName: project.name,
        personalActivityAt: latestActivity(task)
      }))),
      partial: results.some(result => result.result === null),
      hasMore: results.some(({ result }) => result !== null && (result.totalCount ?? result.items.length) > page * pageSize),
      page
    };
    }));
  }
}

export function isOpen(task: PersonalWorkItem): boolean {
  return !task.completedAt;
}

export function isBlocked(task: PersonalWorkItem): boolean {
  return (task.relations ?? []).some(relation => ['blockedby', 'isblockedby', 'dependson'].includes(String(relation.relationType ?? '').toLowerCase()));
}

export function isOverdue(task: PersonalWorkItem, now = Date.now()): boolean {
  return isOpen(task) && !!task.dueDate && new Date(task.dueDate).getTime() < now;
}

export function compareDueDates(left: PersonalWorkItem, right: PersonalWorkItem): number {
  const leftTime = left.dueDate ? new Date(left.dueDate).getTime() : Number.MAX_SAFE_INTEGER;
  const rightTime = right.dueDate ? new Date(right.dueDate).getTime() : Number.MAX_SAFE_INTEGER;
  return leftTime - rightTime;
}

function latestActivity(task: Omit<PersonalWorkItem, 'projectName' | 'personalActivityAt'>): string {
  const dates = [
    task.completedAt,
    ...(task.statusHistory ?? []).map(item => item.changedAt),
    ...(task.comments ?? []).map(item => item.editedAt || item.createdAt),
    ...(task.workLogs ?? []).map(item => item.createdAt)
  ].filter((value): value is string => !!value);
  return dates.sort().reverse()[0] ?? task.dueDate ?? '';
}
