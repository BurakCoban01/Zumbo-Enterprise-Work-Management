import { MobileDevelopmentDraft, MobileDevelopmentRepository, MobileIntegrationRole, MobileWebhookDraft } from './mobile-integrations.models';

export const mobileWebhookScopes = Object.freeze([
  { value: 'work-item.created', label: 'İş oluşturuldu' }, { value: 'work-item.updated', label: 'İş güncellendi' },
  { value: 'work-item.moved', label: 'İş taşındı' }, { value: 'work-item.reordered', label: 'İş sıralandı' },
  { value: 'work-item.archived', label: 'İş arşivlendi' }, { value: 'work-item.restored', label: 'İş geri yüklendi' }
]);
export const mobileDevelopmentProviders = Object.freeze([
  { value: 'GitHub' as const, label: 'GitHub', baseUrl: 'https://api.github.com' },
  { value: 'GitLab' as const, label: 'GitLab', baseUrl: 'https://gitlab.com/api/v4' }
]);

export function hasMobileIntegrationPermission(roles: readonly MobileIntegrationRole[], roleNames: readonly string[], permission: string): boolean {
  return roles.some(role => role.isActive && roleNames.includes(role.name) && role.permissions.some(value => value === '*' || value === permission));
}
export function newMobileWebhookDraft(): MobileWebhookDraft { return { name: '', targetUrl: '', eventScopes: ['work-item.created'] }; }
export function mobileWebhookDraftFrom(value: { name: string; targetUrl: string; eventScopes: readonly string[]; version: number }): MobileWebhookDraft { return { name: value.name, targetUrl: value.targetUrl, eventScopes: [...value.eventScopes], expectedVersion: value.version }; }
export function validateMobileWebhookDraft(draft: MobileWebhookDraft): string | null {
  const name = draft.name.trim(), targetUrl = draft.targetUrl.trim(), scopes = draft.eventScopes.filter(scope => mobileWebhookScopes.some(item => item.value === scope));
  if (!name || name.length > 100) return 'Webhook adı 1 ile 100 karakter arasında olmalıdır.';
  try { const url = new URL(targetUrl); if (!['https:', 'http:'].includes(url.protocol) || !url.hostname || targetUrl.length > 2048) throw new Error(); } catch { return 'Geçerli bir HTTPS uç noktası girin.'; }
  if (!scopes.length) return 'En az bir olay seçin.';
  return null;
}
export function webhookRequest(draft: MobileWebhookDraft): { name: string; targetUrl: string; eventScopes: readonly string[]; expectedVersion?: number } {
  const eventScopes = [...new Set(draft.eventScopes.filter(scope => mobileWebhookScopes.some(item => item.value === scope)))].sort();
  return draft.expectedVersion == null ? { name: draft.name.trim(), targetUrl: draft.targetUrl.trim(), eventScopes } : { name: draft.name.trim(), targetUrl: draft.targetUrl.trim(), eventScopes, expectedVersion: draft.expectedVersion };
}
export function newMobileDevelopmentDraft(): MobileDevelopmentDraft { return { name: '', provider: 'GitHub', baseUrl: 'https://api.github.com', accessToken: '' }; }
export function validateMobileDevelopmentDraft(draft: MobileDevelopmentDraft): string | null {
  const token = draft.accessToken.trim(), name = draft.name.trim();
  if (!name || name.length > 100) return 'Bağlantı adı 1 ile 100 karakter arasında olmalıdır.';
  if (token.length < 16 || token.length > 512 || /\s/.test(token)) return 'Erişim anahtarı 16 ile 512 arasında boşluksuz karakter içermelidir.';
  try { const url = new URL(draft.baseUrl.trim()); if (!['https:', 'http:'].includes(url.protocol) || url.username || url.password || url.search || url.hash || !url.hostname) throw new Error(); } catch { return 'Temel adres geçerli bir HTTP(S) adresi olmalıdır.'; }
  return null;
}
export function developmentRequest(draft: MobileDevelopmentDraft): MobileDevelopmentDraft { return { ...draft, name: draft.name.trim(), baseUrl: draft.baseUrl.trim().replace(/\/+$/, ''), accessToken: draft.accessToken.trim() }; }
export function mobileMappingRequest(projectId: string, repository: MobileDevelopmentRepository | undefined): { projectId: string; externalRepositoryId: string; repositoryName: string; repositoryFullName: string; repositoryUrl: string; defaultBranch: string } | null {
  if (!projectId || !repository?.externalRepositoryId || !repository.name || !repository.fullName || !repository.defaultBranch) return null;
  try { const url = new URL(repository.url); if (url.protocol !== 'https:' || url.username || url.password || url.hash || !url.hostname) return null; } catch { return null; }
  return { projectId, externalRepositoryId: repository.externalRepositoryId, repositoryName: repository.name, repositoryFullName: repository.fullName, repositoryUrl: repository.url, defaultBranch: repository.defaultBranch };
}
export function mobileWebhookScopeLabel(value: string | null | undefined): string { return value === 'webhook.test' ? 'Test teslimatı' : mobileWebhookScopes.find(scope => scope.value === value)?.label ?? 'Bilinmeyen olay'; }
export function mobileDeliveryLabel(value: string): string { return ({ Pending: 'Sırada', Processing: 'Gönderiliyor', Delivered: 'Teslim edildi', DeadLetter: 'Müdahale gerekli' } as Record<string, string>)[value] ?? 'Bilinmiyor'; }
export function mobileHealthLabel(value: string, connected: boolean): string { if (!connected) return 'Bağlantı kesildi'; return ({ Healthy: 'Sağlıklı', Degraded: 'Müdahale gerekli', NotChecked: 'Henüz denetlenmedi' } as Record<string, string>)[value] ?? 'Henüz denetlenmedi'; }
export function mobileSafeUrlLabel(value: string): string { try { const url = new URL(value); return `${url.protocol}//${url.host}${url.pathname === '/' ? '' : url.pathname}${url.search ? '?…' : ''}`; } catch { return 'Geçersiz adres'; } }
export function mobileSafeDeliveryError(value: string | null | undefined): string { return ({ HTTP_400: 'Alıcı isteği kabul etmedi.', HTTP_401: 'Alıcı imzayı veya kimliği kabul etmedi.', HTTP_403: 'Alıcı isteği reddetti.', HTTP_404: 'Alıcı uç noktası bulunamadı.', HTTP_408: 'Alıcı zaman aşımı bildirdi.', HTTP_429: 'Alıcı istek sınırına ulaştı.', HTTP_500: 'Alıcı geçici bir sunucu hatası bildirdi.', HTTP_502: 'Alıcı ağ geçidi hatası bildirdi.', HTTP_503: 'Alıcı geçici olarak kullanılamıyor.', HTTP_504: 'Alıcı ağ geçidi zaman aşımı bildirdi.', REQUEST_TIMEOUT: 'Alıcı yanıt süresini aştı.', RECEIVER_FAILURE: 'Alıcıya teslimat tamamlanamadı.', TARGET_RESOLUTION_FAILED: 'Uç nokta güvenli biçimde çözümlenemedi.', TARGET_ADDRESS_BLOCKED: 'Uç noktanın ağ adresine izin verilmiyor.' } as Record<string, string>)[value ?? ''] ?? ''; }
export function mobileIntegrationError(code: string | undefined, fallback: string): string { return ({ CONCURRENCY_CONFLICT: 'Kayıt başka bir yerde değiştirildi; güncel durum yenilendi.', FORBIDDEN: 'Bu işlem için entegrasyon yönetimi yetkiniz yok.', VALIDATION_ERROR: 'Alanları ve erişim bilgilerini kontrol edin.', WEBHOOK_SUBSCRIPTION_CONFLICT: 'Webhook başka bir yerde değiştirildi; güncel durum yenilendi.' } as Record<string, string>)[code ?? ''] ?? fallback; }
