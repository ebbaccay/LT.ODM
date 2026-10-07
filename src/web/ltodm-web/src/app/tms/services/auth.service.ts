import { HttpClient } from '@angular/common/http';
import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { environment } from '@env/environment';
import { AuthService as AppAuthService } from '../../core/auth/auth.service';

/** Sidebar menu for the signed-in user (GET /api/v1/navigation, nav.* tables filtered by role). */
export interface MenuItemDto {
  itemId: number;
  /** Plain text or a translation key (e.g. nav.garmentQuotation). */
  text: string;
  /** Without the leading slash; '' = home. */
  route: string;
  /** Lucide icon name (lucideReceipt). */
  icon: string;
  sortOrder: number;
}

export interface MenuGroupDto {
  groupId: number;
  text: string;
  icon: string;
  /** 'bottom' groups are pinned to the bottom of the sidebar. */
  slot: 'main' | 'bottom';
  sortOrder: number;
  items: MenuItemDto[];
}

export interface MenuDto {
  groups: MenuGroupDto[];
}

export interface AuthUser {
  username: string;
  firstname: string;
  lastname?: string;
  email?: string;
  token: string;
  location?: string;
  userGroup?: string;
  /** LT ODM roles (auth.Roles), lower-cased like TMS ('admin'). */
  roles: string[];
}

/** Menu load state: null = loading, 'error' = the API call failed. */
type MenuState = MenuDto | 'error' | null;

/**
 * TMS-compatible AuthService for the ported modules. Signing in and out is done by the LT ODM
 * AuthService; this adds the user's sidebar menu, loaded from GET /api/v1/navigation.
 * Roles come from the sign-in token (one role system: auth.Roles), not from a TMS table.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly app = inject(AppAuthService);
  private readonly http = inject(HttpClient);

  private readonly menuState = signal<MenuState>(null);
  private loadedFor: number | null = null;

  readonly user = computed<AuthUser | null>(() => {
    const u = this.app.user();
    if (!u) return null;
    const [firstname, ...rest] = u.displayName.split(' ');
    return {
      username: u.userName,
      firstname: firstname ?? u.userName,
      lastname: rest.join(' ') || undefined,
      email: u.email,
      token: this.app.getAccessToken() ?? '',
      location: u.location ?? undefined,
      userGroup: u.userGroup ?? undefined,
      roles: u.roles.map((r) => r.toLowerCase()),
    };
  });

  readonly isAuthenticated = computed(() => !!this.user());
  readonly isAdmin = computed(() => this.user()?.roles.includes('admin') ?? false);

  /** The user's menu, or null while loading / after an error. */
  readonly menu = computed(() => {
    const s = this.menuState();
    return s && s !== 'error' ? s : null;
  });
  readonly menuLoading = computed(() => this.menuState() === null && !!this.app.user());
  readonly menuFailed = computed(() => this.menuState() === 'error');

  constructor() {
    effect(() => {
      const u = this.app.user();
      if (!u) {
        this.loadedFor = null;
        this.menuState.set(null);
      } else if (this.loadedFor !== u.userId) {
        this.loadedFor = u.userId;
        this.loadNavigation();
      }
    });
  }

  /** TMS role check; role keys are lower-case ('admin', 'merchandiser'). */
  hasRole(role: string): boolean {
    return this.user()?.roles.includes(role.toLowerCase()) ?? false;
  }

  signOut(): void {
    void this.app.logout();
  }

  /** (Re)loads the sidebar menu, e.g. after Settings > Menu changes. */
  loadNavigation(): void {
    this.http.get<MenuDto>(`${environment.apiBaseUrl}/api/v1/navigation`).subscribe({
      next: (menu) => this.menuState.set(menu),
      error: () => this.menuState.set('error'),
    });
  }
}
