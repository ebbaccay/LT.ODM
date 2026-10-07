import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideListChecks, lucidePencil, lucidePlus, lucideSearch, lucideTrash2 } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { RefListInfo, RefListItem, SettingsAdminService, adminErrorMessage } from '@services/settings-admin.service';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { Modal } from '@tms/shared/ui/modal';
import { StyleLibraryService } from '../../../../features/style-library/style-library.service';

interface ItemForm {
  code: string;
  name: string;
  sortOrder: string;
  isActive: boolean;
}

/**
 * Settings > Reference lists: every pick list on the Styles screens (customers, seasons, BOM content classes, UOMs, ...).
 * The API sends each list's rules (GET /admin/ref-lists), so the form and the server check the same things.
 */
@Component({
  selector: 'app-reference-lists',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    TranslocoPipe, NgIcon, Modal, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmCheckboxImports, HlmInputImports, HlmLabelImports,
    HlmNativeSelectImports, HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucidePlus, lucidePencil, lucideTrash2, lucideListChecks, lucideSearch })],
  templateUrl: './reference-lists.html',
})
export class ReferenceLists implements OnInit {
  private readonly svc = inject(SettingsAdminService);
  private readonly styles = inject(StyleLibraryService);
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly transloco = inject(TranslocoService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly lists = signal<RefListInfo[]>([]);
  readonly listKey = signal('');
  readonly list = computed(() => this.lists().find((l) => l.key === this.listKey()) ?? null);

  readonly items = signal<RefListItem[]>([]);
  readonly loading = signal(false);
  readonly search = signal('');
  readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    if (!q) return this.items();
    return this.items().filter((i) => i.code.toLowerCase().includes(q) || (i.name ?? '').toLowerCase().includes(q));
  });

  readonly saving = signal(false);
  readonly dialogOpen = signal(false);
  readonly editing = signal<RefListItem | null>(null);
  readonly touched = signal(false);
  readonly form = signal<ItemForm>({ code: '', name: '', sortOrder: '', isActive: true });

  /** Code in the case the API stores it (what the user sees while typing). */
  readonly code = computed(() => this.applyCase(this.form().code.trim()));
  readonly codeValid = computed(() => {
    const l = this.list();
    if (!l || this.editing()) return true;
    const c = this.code();
    return c.length > 0 && c.length <= l.codeMaxLength && (!l.codePattern || new RegExp(l.codePattern).test(c));
  });
  readonly nameValid = computed(() => {
    const l = this.list();
    const n = this.form().name.trim();
    return !l || ((!l.nameRequired || n.length > 0) && n.length <= l.nameMaxLength);
  });
  readonly sortValid = computed(() => {
    const s = this.form().sortOrder.trim();
    return !this.list()?.hasSortOrder || (/^\d{1,5}$/.test(s) && +s <= 32767);
  });
  readonly dialogTitle = computed(() => (this.editing() ? 'settings.refLists.edit' : 'settings.refLists.add'));

  ngOnInit(): void {
    this.svc.getRefLists().subscribe({
      next: (lists) => {
        this.lists.set(lists);
        this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((p) => {
          const key = lists.some((l) => l.key === p.get('list')) ? p.get('list')! : lists[0]?.key ?? '';
          if (key !== this.listKey()) {
            this.listKey.set(key);
            this.search.set('');
            this.load();
          }
        });
      },
      error: (e) => toast.error(adminErrorMessage(e)),
    });
  }

  pick(key: string): void {
    this.router.navigate([], { relativeTo: this.route, queryParams: { list: key }, replaceUrl: true });
  }

  load(): void {
    const key = this.listKey();
    if (!key) return;
    this.loading.set(true);
    this.svc.getRefList(key).subscribe({
      next: (r) => {
        if (key !== this.listKey()) return; // the user picked another list meanwhile
        this.items.set(r);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(adminErrorMessage(e));
      },
    });
  }

  inUse = (i: RefListItem) => i.usageCount > 0;

  openAdd(): void {
    const next = Math.max(0, ...this.items().map((i) => i.sortOrder ?? 0)) + 1;
    this.editing.set(null);
    this.form.set({ code: '', name: '', sortOrder: String(next), isActive: true });
    this.touched.set(false);
    this.dialogOpen.set(true);
  }

  openEdit(i: RefListItem): void {
    this.editing.set(i);
    this.form.set({ code: i.code, name: i.name ?? '', sortOrder: String(i.sortOrder ?? ''), isActive: i.isActive ?? true });
    this.touched.set(false);
    this.dialogOpen.set(true);
  }

  closeDialog(): void {
    this.dialogOpen.set(false);
  }

  patch<K extends keyof ItemForm>(field: K, value: ItemForm[K]): void {
    this.form.update((f) => ({ ...f, [field]: value }));
  }

  save(): void {
    const l = this.list();
    this.touched.set(true);
    if (!l || !this.codeValid() || !this.nameValid() || !this.sortValid() || this.saving()) return;
    const f = this.form();
    this.saving.set(true);
    this.svc
      .saveRefListItem(l.key, {
        isNew: !this.editing(),
        code: this.editing()?.code ?? this.code(),
        name: f.name.trim(),
        sortOrder: l.hasSortOrder ? +f.sortOrder : null,
        isActive: l.hasIsActive ? f.isActive : null,
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.closeDialog();
          toast.success(this.transloco.translate('settings.refLists.saved'));
          this.styles.refreshLookups();
          this.load();
        },
        error: (e) => {
          this.saving.set(false);
          toast.error(adminErrorMessage(e));
        },
      });
  }

  async confirmDelete(i: RefListItem): Promise<void> {
    const confirmed = await this.confirmDlg.confirm({
      title: this.transloco.translate('settings.refLists.confirmDeleteTitle'),
      message: this.transloco.translate('settings.refLists.confirmDeleteMessage', { code: i.code, name: i.name || i.code }),
      confirmLabel: this.transloco.translate('actions.delete'),
      cancelLabel: this.transloco.translate('actions.cancel'),
      variant: 'danger',
    });
    if (!confirmed) return;
    this.svc.deleteRefListItem(this.listKey(), i.code).subscribe({
      next: () => {
        this.styles.refreshLookups();
        this.load();
      },
      error: (e) => toast.error(adminErrorMessage(e)),
    });
  }

  private applyCase(code: string): string {
    const c = this.list()?.codeCase;
    return c === 'upper' ? code.toUpperCase() : c === 'lower' ? code.toLowerCase() : code;
  }
}
