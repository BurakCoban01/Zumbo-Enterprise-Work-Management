import { MobileDependencyMetric, MobileOperationsRead, MobileOperationsRole, MobileOperationsSnapshot } from './mobile-operations.models';

export function hasMobileOperationsPermission(roles: readonly MobileOperationsRole[], assignedRoles: readonly string[], permission: string): boolean {
  return roles.some(role => role.isActive && assignedRoles.includes(role.name) && role.permissions.some(value => value === '*' || value === permission));
}

export function mobileOperationsReadLabel(read: MobileOperationsRead): string {
  return ({ dependencies: 'Bağımlılık sağlığı', messaging: 'Sistem olayları', messageDeadLetters: 'Sistem olay müdahaleleri', notifications: 'Bildirim teslimatı', notificationDeadLetters: 'Bildirim müdahaleleri', storage: 'Dosya güvenliği' } as const)[read];
}

export function mobileDependencyNeedsAttention(value: MobileDependencyMetric): boolean {
  return value.circuitOpen || value.failed > 0 || value.timedOut > 0;
}

export function mobileDependencyState(value: MobileDependencyMetric): string {
  return mobileDependencyNeedsAttention(value) ? 'Müdahale gerekli' : 'Sağlıklı';
}

export function mobileDependencyLabel(value: string): string {
  const normalized = value.toLowerCase();
  if (normalized.includes('redis')) return 'Önbellek hizmeti';
  if (normalized.includes('postgres') || normalized.includes('database')) return 'Veri hizmeti';
  if (normalized.includes('search')) return 'Arama hizmeti';
  if (normalized.includes('storage')) return 'Dosya hizmeti';
  if (normalized.includes('mail') || normalized.includes('email')) return 'E-posta hizmeti';
  return 'Harici bağımlılık';
}

export function mobileMessageLabel(value: string | null | undefined): string {
  if (!value) return 'Sistem olayı';
  const normalized = value.toLowerCase();
  if (normalized.includes('notification')) return 'Bildirim olayı';
  if (normalized.includes('work')) return 'İş olayı';
  return 'Sistem olayı';
}

export function mobileNotificationLabel(value: string | null | undefined): string {
  if (!value) return 'Bildirim';
  const normalized = value.toLowerCase();
  if (normalized.includes('email')) return 'E-posta bildirimi';
  if (normalized.includes('push')) return 'Anlık bildirim';
  return 'Bildirim';
}

export function mobileOperationsAttentionCount(snapshot: MobileOperationsSnapshot): number {
  const dependencies = snapshot.dependencies?.dependencies.filter(mobileDependencyNeedsAttention).length ?? 0;
  return dependencies + (snapshot.messaging?.deadLetter ?? 0) + (snapshot.notifications?.deadLetter ?? 0) + (snapshot.storage?.quarantined ?? 0);
}

export function mobileOperationsErrorMessage(code: string | undefined, fallback: string): string {
  return ({ FORBIDDEN: 'Bu alan için operasyon yönetimi yetkiniz yok.', UNAUTHORIZED: 'Oturumunuzun süresi dolmuş olabilir. Yeniden giriş yapın.', RATE_LIMITED: 'İşlem sınırına ulaşıldı. Kısa süre sonra yeniden deneyin.' } as Record<string, string>)[code ?? ''] ?? fallback;
}
