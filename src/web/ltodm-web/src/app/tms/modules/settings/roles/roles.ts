import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePencil, lucidePlus, lucideShieldCheck, lucideTrash2 } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { RoleDto, SettingsAdminService, adminErrorMessage } from '@services/settings-admin.service';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { Modal } from '@tms/shared/ui/modal';

interface RoleForm {
  name: string;
  displayName: string;
  description: string;
}

/** Same rule as auth.Roles CK_auth_Roles_Name and AdminValidation.Role. */
const ROLE_NAME = /^[A-Za-z][A-Za-z0-9_]{1,63}$/;

/** Settings > Roles: the LT ODM roles (auth.Roles) used for sign-in, menu access and the ported TMS screens. */
@Component({
  selector: 'app-roles',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    TranslocoPipe, NgIcon, Modal, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmInputImports, HlmLabelImports, HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucidePlus, lucidePencil, lucideTrash2, lucideShieldCheck })],
  templateUrl: './roles.html',
})
export class Roles implements OnInit {
  private readonly svc = inject(SettingsAdminService);
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly transloco = inject(TranslocoService);

  readonly roles = signal<RoleDto[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly dialogOpen = signal(false);
  readonly editingRole = signal<RoleDto | null>(null);
  readonly touched = signal(false);
  readonly form = signal<RoleForm>({ name: '', displayName: '', description: '' });

  readonly nameValid = computed(() => !!this.editingRole() || ROLE_NAME.test(this.form().name.trim()));
  readonly showNameError = computed(() => this.touched() && !this.nameValid());
  readonly dialogTitle = computed(() => (this.editingRole() ? 'settings.roles.editRole' : 'settings.roles.addRole'));

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.svc.getRoles().subscribe({
      next: (r) => {
        this.roles.set(r);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(adminErrorMessage(e));
      },
    });
  }

  openAdd(): void {
    this.editingRole.set(null);
    this.form.set({ name: '', displayName: '', description: '' });
    this.touched.set(false);
    this.dialogOpen.set(true);
  }

  openEdit(role: RoleDto): void {
    this.editingRole.set(role);
    this.form.set({ name: role.name, displayName: role.displayName ?? '', description: role.description ?? '' });
    this.touched.set(false);
    this.dialogOpen.set(true);
  }

  closeDialog(): void {
    this.dialogOpen.set(false);
  }

  patch(field: keyof RoleForm, value: string): void {
    this.form.update((f) => ({ ...f, [field]: value }));
  }

  save(): void {
    this.touched.set(true);
    if (!this.nameValid() || this.saving()) return;
    const f = this.form();
    this.saving.set(true);
    this.svc
      .saveRole({
        roleId: this.editingRole()?.roleId ?? 0,
        name: f.name.trim(),
        displayName: f.displayName.trim() || null,
        description: f.description.trim() || null,
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.closeDialog();
          toast.success(this.transloco.translate('settings.roles.saved'));
          this.load();
        },
        error: (e) => {
          this.saving.set(false);
          toast.error(adminErrorMessage(e));
        },
      });
  }

  /** The Admin role is required by Settings and cannot be deleted (also enforced by auth.usp_Role_Delete). */
  canDelete = (role: RoleDto) => role.name !== 'Admin';

  async confirmDelete(role: RoleDto): Promise<void> {
    const confirmed = await this.confirmDlg.confirm({
      title: this.transloco.translate('settings.roles.confirmDeleteRoleTitle'),
      message: this.transloco.translate('settings.roles.confirmDeleteRoleMessage', { role: role.displayName || role.name, count: role.userCount }),
      confirmLabel: this.transloco.translate('actions.delete'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'danger',
    });
    if (!confirmed) return;
    this.svc.deleteRole(role.name).subscribe({
      next: () => this.load(),
      error: (e) => toast.error(adminErrorMessage(e)),
    });
  }
}
