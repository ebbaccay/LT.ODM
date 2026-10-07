import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEye, lucideEyeOff, lucidePanelsTopLeft, lucidePencil, lucidePlus, lucideTrash2 } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { NavConfigService } from '@services/nav-config.service';
import {
  MenuConfigGroupDto,
  MenuConfigItemDto,
  RoleDto,
  SettingsAdminService,
  adminErrorMessage,
} from '@services/settings-admin.service';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { MENU_ICONS, MENU_ICON_NAMES, menuIcon } from '@tms/shared/ui/menu-icons';
import { Modal } from '@tms/shared/ui/modal';

interface GroupForm {
  text: string;
  icon: string;
  slot: 'main' | 'bottom';
  sortOrder: number;
  isVisible: boolean;
}

interface ItemForm {
  text: string;
  route: string;
  icon: string;
  sortOrder: number;
  isVisible: boolean;
  roles: Set<string>;
}

/** Same rule as nav.Items CK_nav_Items_Route and AdminValidation.Item ('' = home). */
const ROUTE = /^[a-z0-9][a-z0-9/_-]*$/;
const normalizeRoute = (r: string) => r.trim().replace(/^\/+|\/+$/g, '');
const isTranslationKey = (text: string) => /^[A-Za-z]+(\.[A-Za-z0-9]+)+$/.test(text);

/**
 * Settings > Menu: sidebar groups and items (nav.* tables) and which roles see each item.
 * Ported from the TMS NavConfigEditor; the component key is gone (routes decide the screen) and icons are Lucide.
 */
@Component({
  selector: 'app-nav-config-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet, TranslocoPipe, NgIcon, Modal, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmCheckboxImports, HlmInputImports, HlmLabelImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ ...MENU_ICONS, lucidePlus, lucidePencil, lucideTrash2, lucideEye, lucideEyeOff, lucidePanelsTopLeft })],
  templateUrl: './nav-config-editor.html',
})
export class NavConfigEditor implements OnInit {
  private readonly svc = inject(SettingsAdminService);
  private readonly sidebar = inject(NavConfigService);
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly transloco = inject(TranslocoService);

  protected readonly iconNames = MENU_ICON_NAMES;
  protected readonly menuIcon = menuIcon;

  readonly groups = signal<MenuConfigGroupDto[]>([]);
  readonly allRoles = signal<RoleDto[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);

  // Group dialog
  readonly groupDialogOpen = signal(false);
  readonly editingGroup = signal<MenuConfigGroupDto | null>(null);
  readonly groupForm = signal<GroupForm>(this.emptyGroup());
  readonly groupTouched = signal(false);
  readonly groupTextValid = computed(() => this.groupForm().text.trim().length > 0);
  readonly groupDialogTitle = computed(() => (this.editingGroup() ? 'settings.navConfig.editGroup' : 'settings.navConfig.addGroup'));

  // Item dialog
  readonly itemDialogOpen = signal(false);
  readonly editingItem = signal<MenuConfigItemDto | null>(null);
  readonly editingGroupId = signal(0);
  readonly itemForm = signal<ItemForm>(this.emptyItem());
  readonly itemTouched = signal(false);
  readonly itemTextValid = computed(() => this.itemForm().text.trim().length > 0);
  readonly itemRouteValid = computed(() => {
    const r = normalizeRoute(this.itemForm().route);
    return r === '' || (r.length <= 200 && ROUTE.test(r));
  });
  readonly itemDialogTitle = computed(() => (this.editingItem() ? 'settings.navConfig.editItem' : 'settings.navConfig.addItem'));

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    let pending = 2;
    const done = () => --pending === 0 && this.loading.set(false);
    const fail = (e: unknown) => {
      done();
      toast.error(adminErrorMessage(e));
    };
    this.svc.getRoles().subscribe({ next: (r) => (this.allRoles.set(r), done()), error: fail });
    this.svc.getMenu().subscribe({
      next: (g) => {
        this.groups.set(
          [...g].sort((a, b) => a.sortOrder - b.sortOrder).map((x) => ({ ...x, items: [...x.items].sort((a, b) => a.sortOrder - b.sortOrder) })),
        );
        done();
      },
      error: fail,
    });
  }

  /** Text as shown in the sidebar (translation keys such as nav.settings are translated). */
  label = (text: string) => (isTranslationKey(text) ? this.transloco.translate(text) : text);

  pickGroupIcon = (icon: string) => this.patchGroup({ icon });
  pickItemIcon = (icon: string) => this.patchItem({ icon });

  roleLabel = (name: string) => this.allRoles().find((r) => r.name === name)?.displayName || name;

  // ── Groups ──────────────────────────────────────────────────

  patchGroup(patch: Partial<GroupForm>): void {
    this.groupForm.update((f) => ({ ...f, ...patch }));
  }

  openAddGroup(): void {
    this.editingGroup.set(null);
    const next = Math.max(0, ...this.groups().filter((g) => g.slot === 'main').map((g) => g.sortOrder)) + 10;
    this.groupForm.set({ ...this.emptyGroup(), sortOrder: next });
    this.groupTouched.set(false);
    this.groupDialogOpen.set(true);
  }

  openEditGroup(g: MenuConfigGroupDto): void {
    this.editingGroup.set(g);
    this.groupForm.set({ text: g.text, icon: g.icon, slot: g.slot, sortOrder: g.sortOrder, isVisible: g.isVisible });
    this.groupTouched.set(false);
    this.groupDialogOpen.set(true);
  }

  saveGroup(): void {
    this.groupTouched.set(true);
    if (!this.groupTextValid() || this.saving()) return;
    const f = this.groupForm();
    this.persistGroup({ groupId: this.editingGroup()?.groupId ?? 0, ...f, text: f.text.trim() }, () => this.groupDialogOpen.set(false));
  }

  toggleGroupVisibility(g: MenuConfigGroupDto): void {
    this.persistGroup({ groupId: g.groupId, text: g.text, icon: g.icon, slot: g.slot, sortOrder: g.sortOrder, isVisible: !g.isVisible });
  }

  private persistGroup(request: Parameters<SettingsAdminService['saveGroup']>[0], onSaved?: () => void): void {
    this.saving.set(true);
    this.svc.saveGroup(request).subscribe({
      next: () => {
        this.saving.set(false);
        onSaved?.();
        this.changed();
      },
      error: (e) => {
        this.saving.set(false);
        toast.error(adminErrorMessage(e));
      },
    });
  }

  async confirmDeleteGroup(g: MenuConfigGroupDto): Promise<void> {
    const confirmed = await this.confirmDlg.confirm({
      title: this.transloco.translate('settings.navConfig.confirmDeleteGroupTitle'),
      message: this.transloco.translate('settings.navConfig.confirmDeleteGroupMessage', { group: this.label(g.text) }),
      confirmLabel: this.transloco.translate('actions.delete'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'danger',
    });
    if (!confirmed) return;
    this.svc.deleteGroup(g.groupId).subscribe({ next: () => this.changed(), error: (e) => toast.error(adminErrorMessage(e)) });
  }

  // ── Items ───────────────────────────────────────────────────

  patchItem(patch: Partial<ItemForm>): void {
    this.itemForm.update((f) => ({ ...f, ...patch }));
  }

  openAddItem(group: MenuConfigGroupDto): void {
    this.editingItem.set(null);
    this.editingGroupId.set(group.groupId);
    const next = Math.max(0, ...group.items.map((i) => i.sortOrder)) + 10;
    this.itemForm.set({ ...this.emptyItem(), sortOrder: next });
    this.itemTouched.set(false);
    this.itemDialogOpen.set(true);
  }

  openEditItem(item: MenuConfigItemDto, groupId: number): void {
    this.editingItem.set(item);
    this.editingGroupId.set(groupId);
    this.itemForm.set({
      text: item.text,
      route: item.route,
      icon: item.icon,
      sortOrder: item.sortOrder,
      isVisible: item.isVisible,
      roles: new Set(item.allowedRoles),
    });
    this.itemTouched.set(false);
    this.itemDialogOpen.set(true);
  }

  toggleItemRole(name: string, checked: boolean): void {
    this.itemForm.update((f) => {
      const roles = new Set(f.roles);
      if (checked) roles.add(name);
      else roles.delete(name);
      return { ...f, roles };
    });
  }

  saveItem(): void {
    this.itemTouched.set(true);
    if (!this.itemTextValid() || !this.itemRouteValid() || this.saving()) return;
    const f = this.itemForm();
    this.persistItem(
      {
        itemId: this.editingItem()?.itemId ?? 0,
        groupId: this.editingGroupId(),
        text: f.text.trim(),
        route: normalizeRoute(f.route),
        icon: f.icon,
        sortOrder: f.sortOrder,
        isVisible: f.isVisible,
        roles: [...f.roles],
      },
      () => this.itemDialogOpen.set(false),
    );
  }

  toggleItemVisibility(item: MenuConfigItemDto, groupId: number): void {
    this.persistItem({
      itemId: item.itemId,
      groupId,
      text: item.text,
      route: item.route,
      icon: item.icon,
      sortOrder: item.sortOrder,
      isVisible: !item.isVisible,
      roles: item.allowedRoles,
    });
  }

  private persistItem(request: Parameters<SettingsAdminService['saveItem']>[0], onSaved?: () => void): void {
    this.saving.set(true);
    this.svc.saveItem(request).subscribe({
      next: () => {
        this.saving.set(false);
        onSaved?.();
        this.changed();
      },
      error: (e) => {
        this.saving.set(false);
        toast.error(adminErrorMessage(e));
      },
    });
  }

  async confirmDeleteItem(item: MenuConfigItemDto): Promise<void> {
    const confirmed = await this.confirmDlg.confirm({
      title: this.transloco.translate('settings.navConfig.confirmDeleteItemTitle'),
      message: this.transloco.translate('settings.navConfig.confirmDeleteItemMessage', { item: this.label(item.text) }),
      confirmLabel: this.transloco.translate('actions.delete'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'danger',
    });
    if (!confirmed) return;
    this.svc.deleteItem(item.itemId).subscribe({ next: () => this.changed(), error: (e) => toast.error(adminErrorMessage(e)) });
  }

  /** Reload the editor and the sidebar, so the admin sees the change straight away. */
  private changed(): void {
    this.load();
    this.sidebar.reload();
  }

  private emptyGroup(): GroupForm {
    return { text: '', icon: 'lucideFolder', slot: 'main', sortOrder: 10, isVisible: true };
  }

  private emptyItem(): ItemForm {
    return { text: '', route: '', icon: 'lucideCircleDot', sortOrder: 10, isVisible: true, roles: new Set() };
  }
}
