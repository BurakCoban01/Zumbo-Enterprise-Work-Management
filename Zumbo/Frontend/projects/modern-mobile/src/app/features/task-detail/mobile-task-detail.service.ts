import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, catchError, forkJoin, map, of, switchMap } from 'rxjs';
import {
  MobileActivityPage,
  MobileRole,
  MobileTaskActivity,
  MobileTaskAttachment,
  MobileTaskCollaboration,
  MobileTaskComment,
  MobileTaskDetail,
  MobileTaskWorkflow,
  MobileTaskWorkLog,
  MobileUser
} from '../../shell/mobile-workspace.models';
import { MobileTaskDetailContext, MobileTaskDraft, MobileTaskStream, emptyPage } from './mobile-task-detail.models';

@Injectable({ providedIn: 'root' })
export class MobileTaskDetailService {
  private readonly api = inject(ZumboApiClient);

  load(taskId: string): Observable<MobileTaskDetailContext> {
    const id = encodeURIComponent(taskId);
    return this.api.get<MobileTaskDetail>(`/api/work-items/${id}`).pipe(switchMap(detail => {
      const projectId = encodeURIComponent(detail.projectId);
      return forkJoin({
        collaboration: this.api.get<MobileTaskCollaboration>(`/api/work-items/${id}/collaboration`).pipe(catchError(() => of(null))),
        workflow: this.api.get<MobileTaskWorkflow>(`/api/workflows/${projectId}`).pipe(catchError(() => of(null))),
        roles: this.api.get<readonly MobileRole[]>('/api/auth/roles?scope=Project').pipe(catchError(() => of(null))),
        users: this.api.get<readonly MobileUser[]>('/api/auth/users').pipe(catchError(() => of(null))),
        comments: this.stream<MobileTaskComment>(id, 'comments').pipe(catchError(() => of(null))),
        attachments: this.stream<MobileTaskAttachment>(id, 'attachments').pipe(catchError(() => of(null))),
        worklogs: this.stream<MobileTaskWorkLog>(id, 'worklogs').pipe(catchError(() => of(null))),
        activity: this.stream<MobileTaskActivity>(id, 'activity').pipe(catchError(() => of(null)))
      }).pipe(map(value => ({
        detail,
        collaboration: value.collaboration,
        workflow: value.workflow,
        roles: value.roles ?? [],
        users: value.users ?? [],
        comments: value.comments ?? emptyPage(detail.comments ?? []),
        attachments: value.attachments ?? emptyPage(detail.attachments ?? []),
        worklogs: value.worklogs ?? emptyPage(detail.workLogs ?? []),
        activity: value.activity ?? emptyPage<MobileTaskActivity>(),
        partial: Object.values(value).some(item => item === null)
      })));
    }));
  }

  loadStream<T>(taskId: string, stream: MobileTaskStream): Observable<MobileActivityPage<T>> {
    return this.stream<T>(encodeURIComponent(taskId), stream);
  }

  update(detail: MobileTaskDetail, draft: MobileTaskDraft): Observable<MobileTaskDetail> {
    return this.api.put<MobileTaskDetail>(`/api/work-items/${encodeURIComponent(detail.id)}`, {
      title: draft.title.trim(),
      description: draft.description.trim(),
      priority: draft.priority,
      dueDate: draft.dueDate || null
    }, { ifMatch: detail.version });
  }

  move(taskId: string, status: string): Observable<MobileTaskDetail> {
    return this.api.patch<MobileTaskDetail>(`/api/work-items/${encodeURIComponent(taskId)}/status`, { status });
  }

  addComment(taskId: string, body: string): Observable<MobileTaskDetail> {
    return this.api.post<MobileTaskDetail>(`/api/work-items/${encodeURIComponent(taskId)}/comments`, { body, mentions: [] });
  }

  addChecklist(taskId: string, text: string): Observable<MobileTaskDetail> {
    return this.api.post<MobileTaskDetail>(`/api/work-items/${encodeURIComponent(taskId)}/checklist`, { text });
  }

  setChecklist(taskId: string, entryId: string, completed: boolean): Observable<MobileTaskDetail> {
    return this.api.patch<MobileTaskDetail>(`/api/work-items/${encodeURIComponent(taskId)}/checklist/${encodeURIComponent(entryId)}`, { completed });
  }

  setWatching(taskId: string, watching: boolean): Observable<MobileTaskCollaboration> {
    return this.api.put<MobileTaskCollaboration>(`/api/work-items/${encodeURIComponent(taskId)}/watch`, { watching });
  }

  setVoted(taskId: string, voted: boolean): Observable<MobileTaskCollaboration> {
    return this.api.put<MobileTaskCollaboration>(`/api/work-items/${encodeURIComponent(taskId)}/vote`, { voted });
  }

  addWorkLog(taskId: string, userId: string, hours: number, note: string | null): Observable<MobileTaskDetail> {
    return this.api.post<MobileTaskDetail>(`/api/work-items/${encodeURIComponent(taskId)}/worklogs`, { userId, hours, note });
  }

  upload(taskId: string, file: File): Observable<MobileTaskDetail> {
    return this.api.upload<MobileTaskDetail>(`/api/work-items/${encodeURIComponent(taskId)}/attachments/upload`, file);
  }

  download(taskId: string, attachmentId: string): Observable<Blob> {
    return this.api.download(`/api/work-items/${encodeURIComponent(taskId)}/attachments/${encodeURIComponent(attachmentId)}/download`);
  }

  addLabel(taskId: string, label: string): Observable<MobileTaskDetail> {
    return this.api.post<MobileTaskDetail>(`/api/work-items/${encodeURIComponent(taskId)}/labels`, { label });
  }

  removeLabel(taskId: string, label: string): Observable<MobileTaskDetail> {
    return this.api.delete<MobileTaskDetail>(`/api/work-items/${encodeURIComponent(taskId)}/labels/${encodeURIComponent(label)}`);
  }

  private stream<T>(encodedTaskId: string, stream: MobileTaskStream): Observable<MobileActivityPage<T>> {
    return this.api.get<MobileActivityPage<T>>(`/api/work-items/${encodedTaskId}/${stream}?page=1&pageSize=50`);
  }
}
