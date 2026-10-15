import { ChangeDetectionStrategy, Component, computed, inject, input, model, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleHelp, lucideHistory, lucideUsers } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import {
  ImportAppliedAnswer,
  ImportCodeList,
  ImportCustomerGroup,
  ImportNewCodes,
  StyleImportBatch,
  StyleImportService,
  importErrorMessage,
} from '@services/style-import.service';
import { Observable } from 'rxjs';

/** "POLO" -> "Polo" (a starting name for a new customer). */
const titleCase = (word: string) => word.charAt(0) + word.slice(1).toLowerCase();

/**
 * Settings > Import, after upload: the questions that matter before commit, asked in groups.
 *  - values with a suggested existing code, list by list ("63 SAP suppliers match existing ones") -> Use all;
 *  - customer accounts that look like one customer (Skechers accounts -> SKE; 11 POLO accounts) -> map to an existing
 *    customer, group under a new one, or keep separate;
 *  - customers still new are listed, so committing them is a choice.
 * Answers are remembered for later imports unless unticked; answers already remembered are shown and can be forgotten.
 */
@Component({
  selector: 'app-import-decisions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, NgIcon, HlmButtonImports, HlmCardImports, HlmCheckboxImports, HlmInputImports, HlmSpinnerImports],
  providers: [provideIcons({ lucideCircleHelp, lucideHistory, lucideUsers })],
  templateUrl: './import-decisions.html',
})
export class ImportDecisions {
  private readonly svc = inject(StyleImportService);
  private readonly transloco = inject(TranslocoService);

  readonly batchId = input.required<number>();
  readonly codes = input.required<ImportNewCodes>();
  /** BOM rows the commit skips (no material code), and in how many styles. */
  readonly skippedBom = input<{ rows: number; styles: number } | null>(null);
  /** Keep answers for later imports (shared with the New codes tab). */
  readonly remember = model(true);
  /** The batch after an answer (rows rewritten, checked again). */
  readonly changed = output<StyleImportBatch>();
  /** Open the New codes tab on a list. */
  readonly review = output<void>();
  /** Open the warnings (Issues tab). */
  readonly showWarnings = output<void>();

  /** The question being answered (one at a time). */
  readonly busy = signal<string | null>(null);
  /** Groups answered "keep separate" (this page only). */
  readonly kept = signal<ReadonlySet<string>>(new Set());
  /** Per group: existing customer picked, and the new customer's code / name. */
  readonly picked = signal<Record<string, string>>({});
  readonly newCode = signal<Record<string, string>>({});
  readonly newName = signal<Record<string, string>>({});
  /** Per group: the brand code / name for "keep separate, same brand". */
  readonly brandCode = signal<Record<string, string>>({});
  readonly brandName = signal<Record<string, string>>({});
  readonly showApplied = signal(false);

  readonly customers = computed(() => this.codes().options.filter((o) => o.list === 'customer'));
  readonly brands = computed(() => this.codes().options.filter((o) => o.list === 'brand'));

  /** Lists with values that have a suggested existing code (customers are asked about in groups). */
  readonly suggested = computed(() => {
    const byList = new Map<ImportCodeList, { value: string; code: string }[]>();
    for (const c of this.codes().codes)
      if (c.suggestedCode && c.list !== 'customer') byList.set(c.list, [...(byList.get(c.list) ?? []), { value: c.value, code: c.suggestedCode }]);
    return [...byList].map(([list, items]) => ({ list, items }));
  });

  readonly groups = computed(() => (this.codes().groups ?? []).filter((g) => !this.kept().has(g.key)));

  /** Customers that will be added as new on commit (not in an open question). */
  readonly newCustomers = computed(() => {
    const asked = new Set(this.groups().flatMap((g) => g.accounts.map((a) => a.value)));
    return this.codes().codes.filter((c) => c.list === 'customer' && !c.suggestedCode && !asked.has(c.value));
  });

  readonly applied = computed(() => this.codes().applied ?? []);

  readonly anything = computed(
    () => this.suggested().length + this.groups().length + this.newCustomers().length + this.applied().length + (this.skippedBom()?.rows ?? 0) > 0,
  );

  accountsLine = (g: ImportCustomerGroup) => g.accounts.map((a) => a.label ?? a.value).join(' · ');
  rowsOf = (g: ImportCustomerGroup) => g.accounts.reduce((n, a) => n + a.rows, 0);
  examples = (items: { value: string; code: string }[]) => items.slice(0, 3).map((i) => `${i.value} → ${i.code}`).join(', ');
  codeFor = (g: ImportCustomerGroup) => this.newCode()[g.key] ?? g.key;
  /** Default brand: the existing customer they look like (SKE), otherwise the shared word (POLO). */
  brandFor = (g: ImportCustomerGroup) => this.brandCode()[g.key] ?? g.suggestedCode ?? g.key;
  brandNameFor = (g: ImportCustomerGroup) =>
    this.brandName()[g.key] ?? this.brands().find((b) => b.code === this.brandFor(g).trim().toUpperCase())?.name ?? (g.suggestedName ?? titleCase(g.key));
  isExistingBrand = (g: ImportCustomerGroup) => this.brands().some((b) => b.code === this.brandFor(g).trim().toUpperCase());
  nameFor = (g: ImportCustomerGroup) => this.newName()[g.key] ?? titleCase(g.key);

  useAll(list: ImportCodeList, items: { value: string; code: string }[]): void {
    this.run(`list:${list}`, this.svc.mapCodes(this.batchId(), items.map((i) => ({ list, ...i })), this.remember()),
      this.transloco.translate('settings.import.ask.usedAll', { count: items.length }));
  }

  /** Every account of the group -> an existing customer. */
  mapGroup(g: ImportCustomerGroup, code: string): void {
    if (!code) return;
    const items = g.accounts.map((a) => ({ list: 'customer' as const, value: a.value, code }));
    this.run(`group:${g.key}`, this.svc.mapCodes(this.batchId(), items, this.remember()),
      this.transloco.translate('settings.import.ask.grouped', { count: items.length, code }));
  }

  /** Every account of the group -> a new customer (added now). */
  createGroup(g: ImportCustomerGroup): void {
    const code = this.codeFor(g).trim().toUpperCase();
    if (!code) return;
    this.run(`group:${g.key}`, this.svc.groupCustomers(this.batchId(), g.accounts.map((a) => a.value), code, this.nameFor(g).trim(), this.remember()),
      this.transloco.translate('settings.import.ask.grouped', { count: g.accounts.length, code }));
  }

  /** The accounts stay separate customers, all under one brand (added if new). */
  brandGroup(g: ImportCustomerGroup): void {
    const brand = this.brandFor(g).trim().toUpperCase();
    if (!brand) return;
    this.run(`group:${g.key}`,
      this.svc.brandCustomers(this.batchId(), g.accounts.map((a) => a.value), brand, this.brandNameFor(g).trim(), g.suggestedCode, this.remember()),
      this.transloco.translate('settings.import.ask.branded', { count: g.accounts.length, brand }));
  }

  setBrandCode(key: string, code: string): void {
    this.brandCode.update((p) => ({ ...p, [key]: code }));
  }

  setBrandName(key: string, name: string): void {
    this.brandName.update((p) => ({ ...p, [key]: name }));
  }

  keepSeparate(g: ImportCustomerGroup): void {
    this.kept.update((s) => new Set([...s, g.key]));
  }

  forget(a: ImportAppliedAnswer): void {
    this.svc.forgetCode(a.list, a.value).subscribe({
      next: () => toast.success(this.transloco.translate('settings.import.ask.forgotten', { value: a.value })),
      error: (e) => toast.error(importErrorMessage(e).title),
    });
  }

  setPicked(key: string, code: string): void {
    this.picked.update((p) => ({ ...p, [key]: code }));
  }

  setNewCode(key: string, code: string): void {
    this.newCode.update((p) => ({ ...p, [key]: code }));
  }

  setNewName(key: string, name: string): void {
    this.newName.update((p) => ({ ...p, [key]: name }));
  }

  private run(key: string, call: Observable<StyleImportBatch>, done: string): void {
    if (this.busy()) return;
    this.busy.set(key);
    call.subscribe({
      next: (batch) => {
        this.busy.set(null);
        toast.success(done);
        this.changed.emit(batch);
      },
      error: (e) => {
        this.busy.set(null);
        toast.error(importErrorMessage(e).title);
      },
    });
  }
}
