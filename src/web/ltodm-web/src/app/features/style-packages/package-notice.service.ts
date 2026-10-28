import { HttpClient } from '@angular/common/http';
import { Injectable, NgZone, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { environment } from '@env/environment';
import { NotificationHubService } from '@services/notification-hub.service';
import { Subject } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';

/** A client answered through a share link (likes, passes, new or changed comments gathered while unread). */
export interface PackageNotice {
  noticeId: number;
  packageId: number;
  packageName: string;
  shareLabel: string;
  likes: number;
  passes: number;
  comments: number;
  clientName: string | null;
  firstUtc: string;
  lastUtc: string;
  isRead: boolean;
}

/** Roles that can read style packages (the API checks the same). */
const READERS = ['admin', 'merchandiser', 'costing', 'viewer'];

/**
 * Style Packages notices for the bell: kept on the server (so they wait while the user is away) and pushed live through
 * the notifications hub (event PackageNotice) to the package owner and whoever made the link. `answered` tells an open
 * package page to reload.
 */
@Injectable({ providedIn: 'root' })
export class PackageNoticeService {
  private readonly http = inject(HttpClient);
  private readonly hub = inject(NotificationHubService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly zone = inject(NgZone);
  private readonly api = `${environment.apiBaseUrl}/api/v1/package-notices`;

  readonly notices = signal<PackageNotice[]>([]);
  readonly unreadCount = computed(() => this.notices().filter((n) => !n.isRead).length);
  /** Package ids clients just answered on (live). */
  readonly answered = new Subject<number>();

  /** After the hub is started (the bell starts it). Users who cannot read packages get none. */
  init(): void {
    if (!(this.auth.user()?.roles ?? []).some((r) => READERS.includes(r.toLowerCase()))) return;
    this.http.get<PackageNotice[]>(this.api).subscribe({ next: (list) => this.notices.set(list), error: () => undefined });
    this.hub.listen('PackageNotice', (n: PackageNotice) =>
      this.zone.run(() => {
        this.notices.update((list) => [{ ...n, isRead: false }, ...list.filter((x) => x.noticeId !== n.noticeId)].slice(0, 50));
        this.answered.next(n.packageId);
      }),
    );
  }

  markAllRead(): void {
    if (!this.unreadCount()) return;
    this.notices.update((list) => list.map((n) => ({ ...n, isRead: true })));
    this.http.post<void>(`${this.api}/read-all`, {}).subscribe({ error: () => undefined });
  }

  open(n: PackageNotice): void {
    if (!n.isRead) {
      this.notices.update((list) => list.map((x) => (x.noticeId === n.noticeId ? { ...x, isRead: true } : x)));
      this.http.post<void>(`${this.api}/${n.noticeId}/read`, {}).subscribe({ error: () => undefined });
    }
    void this.router.navigate(['/packages', n.packageId]);
  }

  reset(): void {
    this.notices.set([]);
  }
}
