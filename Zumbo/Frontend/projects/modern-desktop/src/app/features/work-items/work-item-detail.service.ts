import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, catchError, forkJoin, map, of } from 'rxjs';
import {
  ProjectWorkflow,
  ProjectWorkItemDetail,
  WorkItemActivityEvent,
  WorkItemActivityPage,
  WorkItemApproval,
  WorkItemAttachment,
  WorkItemCollaboration,
  WorkItemComment,
  WorkItemCustomFieldValue,
  WorkItemDevelopmentLink,
  WorkItemDevelopmentMapping,
  WorkItemSchema,
  WorkItemSprintOption,
  WorkItemSprintPage,
  WorkItemStatusEntry,
  WorkItemWorkLog
} from './project-work-item.models';

export type WorkItemDetailStream = 'activity' | 'attachments' | 'approvals' | 'comments' | 'timeline' | 'worklogs';

export interface CreateDevelopmentLink {
  readonly mappingId: string; readonly kind: string; readonly externalId: string; readonly title: string;
  readonly url: string; readonly branch: string | null; readonly commitSha: string | null; readonly status: string;
}

export interface WorkItemDetailExtensions {
  readonly collaboration: WorkItemCollaboration | null;
  readonly activity: WorkItemActivityPage<WorkItemActivityEvent>;
  readonly attachments: WorkItemActivityPage<WorkItemAttachment>;
  readonly approvals: WorkItemActivityPage<WorkItemApproval>;
  readonly comments: WorkItemActivityPage<WorkItemComment>;
  readonly timeline: WorkItemActivityPage<WorkItemStatusEntry>;
  readonly worklogs: WorkItemActivityPage<WorkItemWorkLog>;
  readonly workflow: ProjectWorkflow | null;
  readonly developmentLinks: readonly WorkItemDevelopmentLink[];
  readonly developmentMappings: readonly WorkItemDevelopmentMapping[];
  readonly schema: WorkItemSchema | null;
  readonly sprints: readonly WorkItemSprintOption[];
  readonly partial: boolean;
}

@Injectable({ providedIn: 'root' })
export class WorkItemDetailService {
  private readonly api = inject(ZumboApiClient);

  load(workItemId: string, projectId: string, includeDevelopmentMappings = false): Observable<WorkItemDetailExtensions> {
    const id = encodeURIComponent(workItemId);
    const project = encodeURIComponent(projectId);
    return forkJoin({
      collaboration: this.api.get<WorkItemCollaboration>(`/api/work-items/${id}/collaboration`).pipe(catchError(() => of(null))),
      activity: this.page<WorkItemActivityEvent>(id, 'activity', 1).pipe(catchError(() => of(null))),
      attachments: this.page<WorkItemAttachment>(id, 'attachments', 1).pipe(catchError(() => of(null))),
      approvals: this.page<WorkItemApproval>(id, 'approvals', 1).pipe(catchError(() => of(null))),
      comments: this.page<WorkItemComment>(id, 'comments', 1).pipe(catchError(() => of(null))),
      timeline: this.page<WorkItemStatusEntry>(id, 'timeline', 1).pipe(catchError(() => of(null))),
      worklogs: this.page<WorkItemWorkLog>(id, 'worklogs', 1).pipe(catchError(() => of(null))),
      workflow: this.api.get<ProjectWorkflow>(`/api/workflows/${project}`).pipe(catchError(() => of(null))),
      developmentLinks: this.api.get<readonly WorkItemDevelopmentLink[]>(`/api/work-items/${id}/development-links`).pipe(catchError(() => of(null))),
      developmentMappings: includeDevelopmentMappings
        ? this.api.get<readonly WorkItemDevelopmentMapping[]>(`/api/work-items/${id}/development-links/mappings`).pipe(catchError(() => of([])))
        : of([] as readonly WorkItemDevelopmentMapping[]),
      schema: this.api.get<WorkItemSchema>(`/api/work-item-schemas/${project}`).pipe(catchError(() => of(null))),
      sprints: this.api.get<WorkItemSprintPage>(`/api/sprints/projects/${project}?pageSize=50`).pipe(catchError(() => of(null)))
    }).pipe(map(value => ({
      collaboration: value.collaboration,
      activity: value.activity ?? emptyPage<WorkItemActivityEvent>(),
      attachments: value.attachments ?? emptyPage<WorkItemAttachment>(),
      approvals: value.approvals ?? emptyPage<WorkItemApproval>(),
      comments: value.comments ?? emptyPage<WorkItemComment>(),
      timeline: value.timeline ?? emptyPage<WorkItemStatusEntry>(),
      worklogs: value.worklogs ?? emptyPage<WorkItemWorkLog>(),
      workflow: value.workflow,
      developmentLinks: value.developmentLinks ?? [],
      developmentMappings: value.developmentMappings ?? [],
      schema: value.schema,
      sprints: value.sprints?.items ?? [],
      partial: Object.values(value).some(item => item === null)
    })));
  }

  loadPage<T>(workItemId: string, stream: WorkItemDetailStream, page: number): Observable<WorkItemActivityPage<T>> {
    return this.page<T>(encodeURIComponent(workItemId), stream, page);
  }

  setWatching(workItemId: string, watching: boolean): Observable<WorkItemCollaboration> {
    return this.api.put<WorkItemCollaboration>(`/api/work-items/${encodeURIComponent(workItemId)}/watch`, { watching });
  }

  setVoted(workItemId: string, voted: boolean): Observable<WorkItemCollaboration> {
    return this.api.put<WorkItemCollaboration>(`/api/work-items/${encodeURIComponent(workItemId)}/vote`, { voted });
  }

  addLabel(workItemId: string, label: string): Observable<ProjectWorkItemDetail> {
    return this.api.post<ProjectWorkItemDetail>(`/api/work-items/${encodeURIComponent(workItemId)}/labels`, { label });
  }

  removeLabel(workItemId: string, label: string): Observable<ProjectWorkItemDetail> {
    return this.api.delete<ProjectWorkItemDetail>(`/api/work-items/${encodeURIComponent(workItemId)}/labels/${encodeURIComponent(label)}`);
  }

  addWorkLog(workItemId: string, userId: string, hours: number, note: string | null): Observable<ProjectWorkItemDetail> {
    return this.api.post<ProjectWorkItemDetail>(`/api/work-items/${encodeURIComponent(workItemId)}/worklogs`, { userId, hours, note });
  }

  link(workItemId: string, relatedWorkItemId: string, relationType: string): Observable<ProjectWorkItemDetail> {
    return this.api.post<ProjectWorkItemDetail>(`/api/work-items/${encodeURIComponent(workItemId)}/relations`, { relatedWorkItemId, relationType });
  }

  unlink(workItemId: string, relatedWorkItemId: string, relationType: string): Observable<ProjectWorkItemDetail> {
    return this.api.delete<ProjectWorkItemDetail>(
      `/api/work-items/${encodeURIComponent(workItemId)}/relations/${encodeURIComponent(relatedWorkItemId)}?relationType=${encodeURIComponent(relationType)}`
    );
  }

  move(workItemId: string, status: string): Observable<ProjectWorkItemDetail> {
    return this.api.patch<ProjectWorkItemDetail>(`/api/work-items/${encodeURIComponent(workItemId)}/status`, { status });
  }

  uploadAttachment(workItemId: string, file: File): Observable<ProjectWorkItemDetail> {
    return this.api.upload<ProjectWorkItemDetail>(`/api/work-items/${encodeURIComponent(workItemId)}/attachments/upload`, file);
  }

  deleteAttachment(workItemId: string, attachmentId: string): Observable<ProjectWorkItemDetail> {
    return this.api.delete<ProjectWorkItemDetail>(`/api/work-items/${encodeURIComponent(workItemId)}/attachments/${encodeURIComponent(attachmentId)}`);
  }

  downloadAttachment(workItemId: string, attachmentId: string): Observable<Blob> {
    return this.api.download(`/api/work-items/${encodeURIComponent(workItemId)}/attachments/${encodeURIComponent(attachmentId)}/download`);
  }

  requestApproval(workItemId: string, targetStatus: string): Observable<ProjectWorkItemDetail> {
    return this.api.post<ProjectWorkItemDetail>(`/api/work-items/${encodeURIComponent(workItemId)}/approvals`, { targetStatus });
  }

  decideApproval(workItemId: string, approvalId: string, approved: boolean, note: string | null): Observable<ProjectWorkItemDetail> {
    return this.api.post<ProjectWorkItemDetail>(`/api/work-items/${encodeURIComponent(workItemId)}/approvals/${encodeURIComponent(approvalId)}/decision`, { approved, note });
  }

  createDevelopmentLink(workItemId: string, request: CreateDevelopmentLink): Observable<WorkItemDevelopmentLink> {
    return this.api.post<WorkItemDevelopmentLink>(`/api/work-items/${encodeURIComponent(workItemId)}/development-links`, request);
  }

  deleteDevelopmentLink(workItemId: string, linkId: string, expectedVersion: number): Observable<void> {
    return this.api.delete<void>(`/api/work-items/${encodeURIComponent(workItemId)}/development-links/${encodeURIComponent(linkId)}?expectedVersion=${expectedVersion}`);
  }

  editComment(workItemId: string, commentId: string, body: string): Observable<ProjectWorkItemDetail> {
    return this.api.put<ProjectWorkItemDetail>(`/api/work-items/${encodeURIComponent(workItemId)}/comments/${encodeURIComponent(commentId)}`, { body });
  }

  deleteComment(workItemId: string, commentId: string): Observable<ProjectWorkItemDetail> {
    return this.api.delete<ProjectWorkItemDetail>(`/api/work-items/${encodeURIComponent(workItemId)}/comments/${encodeURIComponent(commentId)}`);
  }

  setPlanning(workItemId: string, sprintId: string | null, estimatePoints: number | null): Observable<ProjectWorkItemDetail> {
    return this.api.patch<ProjectWorkItemDetail>(`/api/work-items/${encodeURIComponent(workItemId)}/planning`, { sprintId, estimatePoints });
  }

  setCustomFields(workItemId: string, values: readonly WorkItemCustomFieldValue[]): Observable<ProjectWorkItemDetail> {
    return this.api.put<ProjectWorkItemDetail>(`/api/work-items/${encodeURIComponent(workItemId)}/custom-fields`, { values });
  }

  private page<T>(id: string, stream: WorkItemDetailStream, page: number): Observable<WorkItemActivityPage<T>> {
    return this.api.get<WorkItemActivityPage<T>>(`/api/work-items/${id}/${stream}?page=${page}&pageSize=50`);
  }
}

export function emptyPage<T>(): WorkItemActivityPage<T> { return { items: [], page: 0, pageSize: 50, totalCount: 0 }; }
