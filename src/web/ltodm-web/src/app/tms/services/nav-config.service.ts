import { Injectable, computed, inject } from '@angular/core';
import { AuthService, MenuGroupDto } from './auth.service';

/** The signed-in user's sidebar groups, sorted, split into the main list and the groups pinned to the bottom. */
@Injectable({ providedIn: 'root' })
export class NavConfigService {
  private readonly auth = inject(AuthService);

  readonly loading = this.auth.menuLoading;
  readonly failed = this.auth.menuFailed;

  readonly groups = computed<MenuGroupDto[]>(() =>
    [...(this.auth.menu()?.groups ?? [])]
      .sort((a, b) => a.sortOrder - b.sortOrder)
      .map((g) => ({ ...g, items: [...g.items].sort((a, b) => a.sortOrder - b.sortOrder) }))
      .filter((g) => g.items.length > 0),
  );

  readonly mainGroups = computed(() => this.groups().filter((g) => g.slot !== 'bottom'));
  readonly bottomGroups = computed(() => this.groups().filter((g) => g.slot === 'bottom'));

  reload(): void {
    this.auth.loadNavigation();
  }
}
