export interface NotificationItem {
  readonly id: string;
  readonly userId: string;
  readonly type: string;
  readonly message: string;
  readonly read: boolean;
  readonly emailStatus: string;
  readonly createdAt: string;
  readonly category: string;
  readonly actionKind: string;
  readonly sourceKind?: string | null;
  readonly sourceId?: string | null;
  readonly projectId?: string | null;
}

export interface NotificationPage {
  readonly items: readonly NotificationItem[];
  readonly page: number;
  readonly hasMore: boolean;
}

export type InboxMode = 'unread' | 'actions' | 'all';

export function notificationLabel(notification: NotificationItem): string {
  return NOTIFICATION_LABELS[notification.type] ?? 'Bildirim';
}

const NOTIFICATION_LABELS: Readonly<Record<string, string>> = {
  Mention: 'Bahsetme',
  Assignment: 'Atama',
  ApprovalRequest: 'Onay isteği',
  Approval: 'Onay sonucu',
  DueDateReminder: 'Tarih hatırlatması',
  TeamInvitation: 'Ekip daveti'
};
