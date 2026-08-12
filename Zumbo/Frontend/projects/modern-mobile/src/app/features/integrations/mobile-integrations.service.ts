import { Injectable, inject } from '@angular/core';
import { ZumboApiClient } from '@zumbo/modern-shared';
import { Observable, forkJoin } from 'rxjs';
import { MobileDevelopmentConnection, MobileDevelopmentMapping, MobileDevelopmentReceipt, MobileDevelopmentRepository, MobileIntegrationProject, MobileIntegrationRole, MobileWebhook, MobileWebhookDelivery, MobileWebhookMetrics, MobileWebhookReceipt } from './mobile-integrations.models';

@Injectable()
export class MobileIntegrationsService {
  private readonly api = inject(ZumboApiClient);
  roles(): Observable<readonly MobileIntegrationRole[]> { return this.api.get('/api/auth/roles?scope=System'); }
  webhooks(): Observable<readonly MobileWebhook[]> { return this.api.get('/api/integrations/webhooks'); }
  metrics(): Observable<MobileWebhookMetrics> { return this.api.get('/api/integrations/webhooks/metrics'); }
  webhookData(): Observable<{ webhooks: readonly MobileWebhook[]; metrics: MobileWebhookMetrics }> { return forkJoin({ webhooks: this.webhooks(), metrics: this.metrics() }); }
  deliveries(id: string, cursor?: string | null): Observable<{ readonly items: readonly MobileWebhookDelivery[]; readonly nextCursor?: string | null }> { return this.api.get(`/api/integrations/webhooks/${encodeURIComponent(id)}/deliveries?pageSize=30${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`); }
  saveWebhook(draft: { name: string; targetUrl: string; eventScopes: readonly string[]; expectedVersion?: number }, selected?: MobileWebhook | null): Observable<MobileWebhookReceipt | MobileWebhook> { return selected ? this.api.put(`/api/integrations/webhooks/${encodeURIComponent(selected.id)}`, draft, { ifMatch: selected.version }) : this.api.post('/api/integrations/webhooks', draft); }
  rotateWebhook(value: MobileWebhook): Observable<MobileWebhookReceipt> { return this.api.post(`/api/integrations/webhooks/${encodeURIComponent(value.id)}/rotate-secret`, { expectedVersion: value.version }, { ifMatch: value.version }); }
  setWebhookActive(value: MobileWebhook, active: boolean): Observable<MobileWebhook> { return this.api.post(`/api/integrations/webhooks/${encodeURIComponent(value.id)}/${active ? 'enable' : 'disable'}`, { expectedVersion: value.version }, { ifMatch: value.version }); }
  testWebhook(id: string): Observable<MobileWebhookDelivery> { return this.api.post(`/api/integrations/webhooks/${encodeURIComponent(id)}/test-delivery`, {}); }
  replayDelivery(id: string): Observable<MobileWebhookDelivery> { return this.api.post(`/api/integrations/webhooks/deliveries/${encodeURIComponent(id)}/replay`, {}); }
  development(): Observable<readonly MobileDevelopmentConnection[]> { return this.api.get('/api/integrations/development'); }
  developmentConnection(id: string): Observable<MobileDevelopmentConnection> { return this.api.get(`/api/integrations/development/${encodeURIComponent(id)}`); }
  projects(organizationId: string): Observable<readonly MobileIntegrationProject[]> { return this.api.get(`/api/projects?organizationId=${encodeURIComponent(organizationId)}`); }
  developmentData(organizationId: string): Observable<{ development: readonly MobileDevelopmentConnection[]; projects: readonly MobileIntegrationProject[] }> { return forkJoin({ development: this.development(), projects: this.projects(organizationId) }); }
  mappings(id: string): Observable<readonly MobileDevelopmentMapping[]> { return this.api.get(`/api/integrations/development/${encodeURIComponent(id)}/mappings`); }
  repositories(id: string): Observable<{ readonly items: readonly MobileDevelopmentRepository[]; readonly sourceStatus?: string | null }> { return this.api.get(`/api/integrations/development/${encodeURIComponent(id)}/repositories`); }
  createDevelopment(draft: { name: string; provider: string; baseUrl: string; accessToken: string }): Observable<MobileDevelopmentReceipt> { return this.api.post('/api/integrations/development', draft); }
  createMapping(connectionId: string, request: unknown): Observable<MobileDevelopmentMapping> { return this.api.post(`/api/integrations/development/${encodeURIComponent(connectionId)}/mappings`, request); }
  deleteMapping(mapping: MobileDevelopmentMapping): Observable<void> { return this.api.delete(`/api/integrations/development/mappings/${encodeURIComponent(mapping.id)}?expectedVersion=${encodeURIComponent(mapping.version)}`, { ifMatch: mapping.version }); }
  health(value: MobileDevelopmentConnection): Observable<MobileDevelopmentConnection> { return this.api.post(`/api/integrations/development/${encodeURIComponent(value.id)}/health`, { expectedVersion: value.version }, { ifMatch: value.version }); }
  rotateCredential(value: MobileDevelopmentConnection, accessToken: string): Observable<MobileDevelopmentConnection> { return this.api.post(`/api/integrations/development/${encodeURIComponent(value.id)}/rotate-credential`, { accessToken, expectedVersion: value.version }, { ifMatch: value.version }); }
  rotateDevelopmentSecret(value: MobileDevelopmentConnection): Observable<MobileDevelopmentReceipt> { return this.api.post(`/api/integrations/development/${encodeURIComponent(value.id)}/rotate-webhook-secret`, { expectedVersion: value.version }, { ifMatch: value.version }); }
  disconnect(value: MobileDevelopmentConnection): Observable<MobileDevelopmentConnection> { return this.api.post(`/api/integrations/development/${encodeURIComponent(value.id)}/disconnect`, { expectedVersion: value.version }, { ifMatch: value.version }); }
  deleteDevelopment(value: MobileDevelopmentConnection): Observable<void> { return this.api.delete(`/api/integrations/development/${encodeURIComponent(value.id)}?expectedVersion=${encodeURIComponent(value.version)}`, { ifMatch: value.version }); }
}
