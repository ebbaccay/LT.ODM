import { Injectable, inject, signal, computed, NgZone } from '@angular/core';
import { NotificationHubService } from './notification-hub.service';
import { UserInfoService } from './user-info.service';
import { Tone } from '@tms/shared/ui/tones';

export type GqNotifType = 'remark' | 'sent' | 'submission' | 'approval' | 'rejection' | 'recall' | 'revision';

export interface GqNotification {
  id:          string;
  type:        GqNotifType;
  styleId:     string;
  packName:    string;
  factoryId:   string;
  factoryName: string;
  sender:      string;
  text:        string;
  ts:          string;
  read:        boolean;
}

const TYPE_LABEL: Record<GqNotifType, string> = {
  remark:     'Message',
  sent:       'Sent to Factory',
  submission: 'Factory Submitted',
  approval:   'Approved',
  rejection:  'Rejected',
  recall:     'Recalled',
  revision:   'For Revision',
};

/** Colour family per type (tms/shared/ui/tones.ts). */
const TYPE_TONE: Record<GqNotifType, Tone> = {
  remark:     'primary',
  sent:       'violet',
  submission: 'amber',
  approval:   'green',
  rejection:  'red',
  recall:     'neutral',
  revision:   'violet',
};

@Injectable({ providedIn: 'root' })
export class GqNotificationService {
  private readonly _notifHub = inject(NotificationHubService);
  private readonly _userInfo = inject(UserInfoService);
  private readonly _zone = inject(NgZone);

  readonly all         = signal<GqNotification[]>([]);
  readonly unreadCount = computed(() => this.all().filter(n => !n.read).length);

  typeLabel(type: GqNotifType): string { return TYPE_LABEL[type] ?? type; }
  typeTone(type: GqNotifType): Tone { return TYPE_TONE[type] ?? 'neutral'; }

  /** Call once from the Layout component's ngOnInit. */
  async init(): Promise<void> {
    await this._notifHub.initHub();

    const hubConn = (this._notifHub as any)['hubConn'];
    if (!hubConn) return;

    hubConn.off('GqNotification');
    hubConn.on('GqNotification', (payload: any) => {
      this._zone.run(() => {
        const userGroup = this._userInfo.user?.userGroup ?? 'TMS';
        const location  = this._userInfo.user?.location  ?? null;

        // FTY users must only receive notifications for their own factory.
        if (userGroup !== 'TMS' && location && (payload.factoryId ?? '') !== location) return;

        const n: GqNotification = {
          id:          payload.id ?? `gqn_${Date.now()}_${Math.random().toString(36).slice(2)}`,
          type:        (payload.type      ?? 'remark') as GqNotifType,
          styleId:     payload.styleId   ?? '',
          packName:    payload.packName  ?? '',
          factoryId:   payload.factoryId ?? '',
          factoryName: payload.factoryName ?? '',
          sender:      payload.sender    ?? '',
          text:        payload.text      ?? '',
          ts:          payload.ts ?? new Date().toLocaleString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }),
          read:        false,
        };
        this.all.update(list => {
          if (list.some(e => e.id === n.id)) return list;
          return [n, ...list].slice(0, 50);
        });
      });
    });

    hubConn.off('NotificationHistory');
    hubConn.on('NotificationHistory', (json: string) => {
      this._zone.run(() => {
        try {
          const historyData = JSON.parse(json);
          if (!Array.isArray(historyData)) return;
          const userGroup = this._userInfo.user?.userGroup ?? 'TMS';
          const location  = this._userInfo.user?.location  ?? null;
          const lastRead = this._lastReadTs();
          const notifications: GqNotification[] = historyData
            .filter((row: any) =>
              userGroup === 'TMS' || !location || (row.factory_id ?? '') === location
            )
            .map((row: any) => ({
            id:          `gqn_${row.notification_id}`,
            type:        (row.type ?? 'remark') as GqNotifType,
            styleId:     row.style_id    ?? '',
            packName:    row.pack_name   ?? '',
            factoryId:   row.factory_id  ?? '',
            factoryName: row.factory_name ?? '',
            sender:      row.sender      ?? '',
            text:        row.message_text ?? '',
            ts:          row.created_at  ?? '',
            read:        lastRead ? new Date(row.created_at) <= new Date(lastRead) : false,
          }));
          this.all.update(list => {
            const existingIds = new Set(list.map(n => n.id));
            const fresh = notifications.filter(n => !existingIds.has(n.id));
            return [...fresh, ...list].slice(0, 50);
          });
        } catch (e) {
          console.error('Failed to parse notification history:', e);
        }
      });
    });

    const userGroup = this._userInfo.user?.userGroup ?? 'TMS';
    const location  = this._userInfo.user?.location  ?? null;
    const recipientGroup = userGroup === 'TMS' ? 'TMS' : `FTY_${location}`;

    await this._notifHub.loadNotificationHistory(recipientGroup, 7);
  }

  private _lastReadKey(): string {
    return `gq_notif_read_${this._userInfo.user?.username ?? 'anon'}`;
  }

  private _lastReadTs(): string | null {
    return localStorage.getItem(this._lastReadKey());
  }

  markAllRead(): void {
    localStorage.setItem(this._lastReadKey(), new Date().toISOString());
    this.all.update(list => list.map(n => ({ ...n, read: true })));
  }

  markRead(id: string): void {
    this.all.update(list => list.map(n => n.id === id ? { ...n, read: true } : n));
  }

  clear(): void { this.all.set([]); }

  /** Call on sign-out to purge in-memory notifications and close the hub connection. */
  async reset(): Promise<void> {
    this.all.set([]);
    await this._notifHub.teardownHub();
  }
}
