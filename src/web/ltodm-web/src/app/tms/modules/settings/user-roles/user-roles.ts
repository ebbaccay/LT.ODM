import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideKeyRound, lucideMail, lucideSearch, lucideTrash2, lucideUserCheck, lucideUserCog, lucideUserPlus, lucideUserX, lucideUsers, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { AuthService } from '../../../../core/auth/auth.service';
import { evaluatePassword } from '../../../../core/auth/password-rules';
import { PasswordInput } from '../../../../features/auth/password-input';
import { PasswordStrength } from '../../../../features/auth/password-strength';
import { RoleDto, SettingsAdminService, UserAccessDto, adminErrorMessage } from '@services/settings-admin.service';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { Modal } from '@tms/shared/ui/modal';

interface AccessForm {
  roles: Set<string>;
  userGroup: string;
  location: string;
}

interface NewUserForm extends AccessForm {
  userName: string;
  email: string;
  displayName: string;
  /** 'link' = email a "set your password" link; 'temporary' = the admin sets a password the user must change. */
  passwordMode: 'link' | 'temporary';
}

/** Same rules as auth.Users (CK_auth_Users_UserName / CK_auth_Users_Email) and AdminValidation.NewUser. */
const USER_NAME = /^[A-Za-z0-9._-]{3,64}$/;
const EMAIL = /^[A-Za-z0-9._%+'-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}$/;

/**
 * Settings > User roles: add users, and set each user's roles plus the TMS user group ('FTY' = factory user) and
 * location (factory code) used by the ported TMS screens. Access changes apply at the user's next token refresh
 * (within 15 minutes). New users get an email with a one-time link to set their own password; administrators never
 * can send a new link (expired invite, forgotten password, locked account). Where email is not available (e.g. no SMTP
 * yet) the administrator can set a temporary password instead, when adding the user or later; the user must change it
 * at first sign-in. From the Edit access dialog a user can also be deactivated (signed out, cannot sign in),
 * reactivated, or removed for good; never one's own account (the API refuses it too).
 */
@Component({
  selector: 'app-user-roles',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, TranslocoPipe, NgIcon, Modal, ReactiveFormsModule, PasswordInput, PasswordStrength, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmCheckboxImports, HlmInputImports,
    HlmLabelImports, HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideSearch, lucideUserCog, lucideUserPlus, lucideUsers, lucideMail, lucideX, lucideUserX, lucideUserCheck, lucideTrash2, lucideKeyRound })],
  templateUrl: './user-roles.html',
})
export class UserRoles implements OnInit {
  private readonly svc = inject(SettingsAdminService);
  private readonly auth = inject(AuthService);
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly transloco = inject(TranslocoService);
  protected readonly passwordPolicy = this.auth.passwordPolicy;

  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly searchQuery = signal('');
  readonly users = signal<UserAccessDto[]>([]);
  readonly availableRoles = signal<RoleDto[]>([]);

  // Edit access dialog
  readonly editingUser = signal<UserAccessDto | null>(null);
  readonly form = signal<AccessForm>({ roles: new Set(), userGroup: '', location: '' });

  // Add user dialog
  readonly createOpen = signal(false);
  readonly createTouched = signal(false);
  readonly newUser = signal<NewUserForm>(this.emptyNewUser());
  readonly newPassword = new FormControl('', { nonNullable: true });
  readonly newPasswordValue = toSignal(this.newPassword.valueChanges, { initialValue: '' });
  readonly newUserPersonalInfo = computed(() => {
    const u = this.newUser();
    return [u.userName, u.email, u.displayName];
  });
  readonly newUserErrors = computed(() => {
    const u = this.newUser();
    return {
      userName: !USER_NAME.test(u.userName.trim()),
      email: u.email.trim().length > 256 || !EMAIL.test(u.email.trim()),
      displayName: !u.displayName.trim() || u.displayName.trim().length > 128,
      password: u.passwordMode === 'temporary' && !passwordOk(this.newPasswordValue(), this.passwordPolicy(), this.newUserPersonalInfo()),
    };
  });

  // Temporary password dialog (existing user)
  readonly tempPasswordUser = signal<UserAccessDto | null>(null);
  readonly tempTouched = signal(false);
  readonly tempPassword = new FormControl('', { nonNullable: true });
  readonly tempPasswordValue = toSignal(this.tempPassword.valueChanges, { initialValue: '' });
  readonly tempPersonalInfo = computed(() => {
    const u = this.tempPasswordUser();
    return u ? [u.userName, u.email, u.displayName] : [];
  });
  readonly tempPasswordValid = computed(() => passwordOk(this.tempPasswordValue(), this.passwordPolicy(), this.tempPersonalInfo()));
  readonly newUserValid = computed(() => !Object.values(this.newUserErrors()).some(Boolean));

  readonly filteredUsers = computed(() => {
    const q = this.searchQuery().trim().toLowerCase();
    if (!q) return this.users();
    return this.users().filter((u) =>
      [u.userName, u.displayName, u.email, u.location ?? '', ...u.roles].some((v) => v.toLowerCase().includes(q)),
    );
  });

  /** An administrator cannot take away their own Admin role (the API refuses it too). */
  readonly isSelf = computed(() => this.editingUser()?.userId === this.auth.user()?.userId);

  ngOnInit(): void {
    this.load();
    void this.auth.loadPasswordPolicy();
  }

  load(): void {
    this.loading.set(true);
    let pending = 2;
    const done = () => --pending === 0 && this.loading.set(false);
    const fail = (e: unknown) => {
      done();
      toast.error(adminErrorMessage(e));
    };
    this.svc.getRoles().subscribe({ next: (r) => (this.availableRoles.set(r), done()), error: fail });
    this.svc.getUsers().subscribe({ next: (u) => (this.users.set(u), done()), error: fail });
  }

  roleLabel = (name: string) => {
    const role = this.availableRoles().find((r) => r.name === name);
    return role?.displayName || name;
  };

  isLocked = (u: UserAccessDto) => !!u.lockoutEndUtc && new Date(u.lockoutEndUtc) > new Date();

  // ── Edit access ─────────────────────────────────────────────

  openAccess(user: UserAccessDto): void {
    this.editingUser.set(user);
    this.form.set({ roles: new Set(user.roles), userGroup: user.userGroup ?? '', location: user.location ?? '' });
  }

  closeDialog(): void {
    this.editingUser.set(null);
  }

  toggleRole(name: string, checked: boolean): void {
    if (name === 'Admin' && !checked && this.isSelf()) return;
    this.form.update((f) => ({ ...f, roles: toggled(f.roles, name, checked) }));
  }

  patch(field: 'userGroup' | 'location', value: string): void {
    this.form.update((f) => ({ ...f, [field]: value }));
  }

  save(): void {
    const user = this.editingUser();
    if (!user || this.saving()) return;
    const f = this.form();
    this.saving.set(true);
    this.svc
      .setUserAccess(user.userId, {
        roles: [...f.roles],
        userGroup: f.userGroup.trim() || null,
        location: f.location.trim() || null,
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.closeDialog();
          toast.success(this.transloco.translate('settings.userRoles.saved'), {
            description: this.transloco.translate('settings.userRoles.savedHint'),
          });
          this.load();
        },
        error: (e) => {
          this.saving.set(false);
          toast.error(adminErrorMessage(e));
        },
      });
  }

  // ── Temporary password ──────────────────────────────────────

  openTempPassword(user: UserAccessDto): void {
    this.closeDialog();
    this.tempPassword.reset('');
    this.tempTouched.set(false);
    this.tempPasswordUser.set(user);
  }

  saveTempPassword(): void {
    const user = this.tempPasswordUser();
    this.tempTouched.set(true);
    if (!user || !this.tempPasswordValid() || this.saving()) return;
    this.saving.set(true);
    this.svc.setTemporaryPassword(user.userId, this.tempPassword.value).subscribe({
      next: () => {
        this.saving.set(false);
        this.tempPasswordUser.set(null);
        this.tempPassword.reset('');
        toast.success(this.transloco.translate('settings.userRoles.tempPasswordSet', { name: user.displayName }), {
          description: this.transloco.translate('settings.userRoles.tempPasswordGiveHint'),
        });
        this.load();
      },
      error: (e) => {
        this.saving.set(false);
        toast.error(adminErrorMessage(e));
      },
    });
  }

  // ── Deactivate / remove ─────────────────────────────────────

  async setActive(user: UserAccessDto, isActive: boolean): Promise<void> {
    if (this.saving()) return;
    if (!isActive) {
      const confirmed = await this.confirmDlg.confirm({
        title: this.transloco.translate('settings.userRoles.deactivateTitle'),
        message: this.transloco.translate('settings.userRoles.deactivateMessage', { name: user.displayName }),
        confirmLabel: this.transloco.translate('settings.userRoles.deactivate'),
        cancelLabel: this.transloco.translate('actions.cancel'),
        variant: 'warning',
      });
      if (!confirmed) return;
    }
    this.saving.set(true);
    this.svc.setUserActive(user.userId, isActive).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeDialog();
        toast.success(this.transloco.translate(isActive ? 'settings.userRoles.activated' : 'settings.userRoles.deactivated', { name: user.displayName }));
        this.load();
      },
      error: (e) => {
        this.saving.set(false);
        toast.error(adminErrorMessage(e));
      },
    });
  }

  async remove(user: UserAccessDto): Promise<void> {
    if (this.saving()) return;
    const confirmed = await this.confirmDlg.confirm({
      title: this.transloco.translate('settings.userRoles.removeTitle'),
      message: this.transloco.translate('settings.userRoles.removeMessage', { name: user.displayName, userName: user.userName }),
      confirmLabel: this.transloco.translate('actions.remove'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'danger',
    });
    if (!confirmed) return;
    this.saving.set(true);
    this.svc.deleteUser(user.userId).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeDialog();
        toast.success(this.transloco.translate('settings.userRoles.removed', { name: user.displayName }));
        this.load();
      },
      error: (e) => {
        this.saving.set(false);
        toast.error(adminErrorMessage(e));
      },
    });
  }

  // ── Add user ────────────────────────────────────────────────

  openCreate(): void {
    this.newUser.set(this.emptyNewUser());
    this.newPassword.reset('');
    this.createTouched.set(false);
    this.createOpen.set(true);
  }

  patchNew(field: 'userName' | 'email' | 'displayName' | 'userGroup' | 'location', value: string): void {
    this.newUser.update((u) => ({ ...u, [field]: value }));
  }

  setPasswordMode(mode: 'link' | 'temporary'): void {
    this.newUser.update((u) => ({ ...u, passwordMode: mode }));
  }

  toggleNewRole(name: string, checked: boolean): void {
    this.newUser.update((u) => ({ ...u, roles: toggled(u.roles, name, checked) }));
  }

  create(): void {
    this.createTouched.set(true);
    if (!this.newUserValid() || this.saving()) return;
    const u = this.newUser();
    const temporary = u.passwordMode === 'temporary';
    this.saving.set(true);
    this.svc
      .createUser({
        userName: u.userName.trim(),
        email: u.email.trim(),
        displayName: u.displayName.trim(),
        roles: [...u.roles],
        userGroup: u.userGroup.trim() || null,
        location: u.location.trim() || null,
        temporaryPassword: temporary ? this.newPassword.value : null,
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.createOpen.set(false);
          this.newPassword.reset('');
          toast.success(this.transloco.translate('settings.userRoles.created', { name: u.displayName.trim() }), {
            description: temporary
              ? this.transloco.translate('settings.userRoles.tempPasswordGiveHint')
              : this.transloco.translate('settings.userRoles.createdHint', { email: u.email.trim() }),
          });
          this.load();
        },
        error: (e) => {
          this.saving.set(false);
          toast.error(adminErrorMessage(e));
        },
      });
  }

  // ── Password link ───────────────────────────────────────────

  async sendPasswordLink(user: UserAccessDto): Promise<void> {
    const confirmed = await this.confirmDlg.confirm({
      title: this.transloco.translate('settings.userRoles.sendLinkTitle'),
      message: this.transloco.translate('settings.userRoles.sendLinkMessage', { name: user.displayName, email: user.email }),
      confirmLabel: this.transloco.translate('actions.send'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'info',
    });
    if (!confirmed) return;
    this.svc.sendPasswordLink(user.userId).subscribe({
      next: () => toast.success(this.transloco.translate('settings.userRoles.linkSent', { email: user.email })),
      error: (e) => toast.error(adminErrorMessage(e)),
    });
  }

  onSearch = (e: Event) => this.searchQuery.set((e.target as HTMLInputElement).value);

  private emptyNewUser(): NewUserForm {
    return { userName: '', email: '', displayName: '', roles: new Set(), userGroup: '', location: '', passwordMode: 'link' };
  }
}

/** Same rules as the API's PasswordPolicy (which also refuses common passwords and is the final check). */
function passwordOk(password: string, policy: Parameters<typeof evaluatePassword>[1], personalInfo: string[]): boolean {
  return evaluatePassword(password, policy, personalInfo).every((r) => r.passed);
}

function toggled(set: Set<string>, name: string, on: boolean): Set<string> {
  const next = new Set(set);
  if (on) next.add(name);
  else next.delete(name);
  return next;
}
