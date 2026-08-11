import { PersonalWorkItem } from '../personal-work/personal-work.models';
import { NotificationItem } from '../notifications/notification.models';

export interface HomeData {
  readonly tasks: readonly PersonalWorkItem[];
  readonly notifications: readonly NotificationItem[];
  readonly partial: boolean;
}

export type { PersonalWorkItem } from '../personal-work/personal-work.models';
