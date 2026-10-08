import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleCheck, lucideCloud, lucideCpu, lucidePencil, lucidePlug, lucidePlus, lucideServer, lucideTrash2, lucideTriangleAlert } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ConfirmDialogService } from '@tms/shared/confirm-dialog/confirm-dialog.service';
import { Modal } from '@tms/shared/ui/modal';
import { problemMessage } from '../../core/auth/auth.service';
import { AI_PURPOSES, AiConnectionItem, AiConnectionKind, AiPurpose, AiSettings, AiStudioService, AiTestResult } from './ai-studio.service';

interface ConnectionForm {
  name: string;
  kind: AiConnectionKind;
  endpoint: string;
  apiKey: string;
  clearApiKey: boolean;
  inHouse: boolean;
  timeoutSeconds: number;
  notes: string;
  isActive: boolean;
}

interface PurposeDraft {
  connectionId: string;
  model: string;
}

/** Address each kind usually has (shown as the placeholder; Gemini's is filled in). */
/** Select value for "Off" (the job is turned off). */
const OFF = 'off';

const ENDPOINTS: Record<AiConnectionKind, string> = {
  Gemini: 'https://generativelanguage.googleapis.com/v1beta',
  OpenAiCompatible: 'http://ai-server.lt.local:8000/v1',
  Custom: 'http://ai-server.lt.local:9000',
};

/**
 * Settings > AI connections (Admin): the AI services the app may call (address, API key, in-house or cloud) and which
 * service + model each job uses. Text and Image are used by AI Studio today; Embedding, Vision, Document and Prediction
 * are prepared for AI Lab capabilities. API keys are write-only here: the screen only ever sees the last 4 characters.
 */
@Component({
  selector: 'app-ai-connections',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, NgIcon, Modal, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmInputImports, HlmNativeSelectImports, HlmSkeletonImports,
    HlmSpinnerImports, TranslocoPipe,
  ],
  providers: [
    provideIcons({ lucideCircleCheck, lucideCloud, lucideCpu, lucidePencil, lucidePlug, lucidePlus, lucideServer, lucideTrash2, lucideTriangleAlert }),
  ],
  templateUrl: './ai-connections.html',
})
export class AiConnections implements OnInit {
  private readonly svc = inject(AiStudioService);
  private readonly confirmDlg = inject(ConfirmDialogService);
  private readonly transloco = inject(TranslocoService);

  readonly settings = signal<AiSettings | null>(null);
  readonly loading = signal(true);

  // Jobs
  readonly drafts = signal<Record<string, PurposeDraft>>({});
  readonly savingPurpose = signal<AiPurpose | null>(null);
  /** Models a tested connection offered, by connection id (for the model pick list). */
  readonly modelsByConnection = signal<Record<number, string[]>>({});

  // Connection dialog
  readonly dialogOpen = signal(false);
  readonly editing = signal<AiConnectionItem | null>(null);
  readonly form = signal<ConnectionForm>(this.blank());
  readonly saving = signal(false);
  readonly testing = signal(false);
  readonly testResult = signal<AiTestResult | null>(null);
  readonly fieldErrors = signal<Record<string, string>>({});

  readonly activeConnections = computed(() => (this.settings()?.connections ?? []).filter((c) => c.isActive));
  readonly endpointPlaceholder = computed(() => ENDPOINTS[this.form().kind]);

  ngOnInit(): void {
    this.load();
  }

  /** Reloads from the server. Rows with unsaved changes keep them (except `reset`, the row just saved). */
  load(reset?: AiPurpose): void {
    this.svc.settings().subscribe({
      next: (s) => {
        const unsaved = AI_PURPOSES.filter((p) => p !== reset && this.dirty(p));
        const current = this.drafts();
        this.settings.set(s);
        this.drafts.set(
          Object.fromEntries(
            s.purposes.map((p) => [
              p.purpose,
              unsaved.includes(p.purpose) ? current[p.purpose] : { connectionId: this.savedChoice(p), model: p.model ?? '' },
            ]),
          ),
        );
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        toast.error(problemMessage(e));
      },
    });
  }

  // ── Jobs ───────────────────────────────────────────────────

  fallbackFor = (purpose: AiPurpose) => this.settings()?.fallbacks.find((f) => f.purpose === purpose) ?? null;
  connectionById = (id: string | number | null) => (this.settings()?.connections ?? []).find((c) => String(c.connectionId) === String(id)) ?? null;

  setDraft(purpose: AiPurpose, patch: Partial<PurposeDraft>): void {
    this.drafts.update((d) => ({ ...d, [purpose]: { ...d[purpose], ...patch } }));
  }

  /** The select's value for a saved job: 'off', a connection id, or '' (app default / not set). */
  private savedChoice = (p: { connectionId: number | null; disabled: boolean }) => (p.disabled ? OFF : p.connectionId ? String(p.connectionId) : '');

  dirty(purpose: AiPurpose): boolean {
    const p = this.settings()?.purposes.find((x) => x.purpose === purpose);
    const d = this.drafts()[purpose];
    if (!p || !d) return false;
    return d.connectionId !== this.savedChoice(p) || (this.isConnection(d.connectionId) && d.model.trim() !== (p.model ?? ''));
  }

  isConnection = (choice: string) => choice !== '' && choice !== OFF;

  savePurpose(purpose: AiPurpose): void {
    const d = this.drafts()[purpose];
    if (!d) return;
    if (this.isConnection(d.connectionId) && !d.model.trim()) {
      toast.error(this.t('aiSettings.modelRequired'));
      return;
    }
    this.savingPurpose.set(purpose);
    const conn = this.isConnection(d.connectionId);
    this.svc.savePurpose(purpose, conn ? Number(d.connectionId) : null, conn ? d.model.trim() : null, d.connectionId === OFF).subscribe({
      next: () => {
        this.savingPurpose.set(null);
        this.svc.refreshStatus();
        toast.success(this.t('aiSettings.jobSaved', { job: this.t('aiSettings.purpose.' + purpose + '.title') }));
        this.load(purpose);
      },
      error: (e) => {
        this.savingPurpose.set(null);
        toast.error(problemMessage(e));
      },
    });
  }

  /** Lists the models of the connection chosen for a job (tests it with its saved key). */
  loadModels(connectionId: string): void {
    const c = this.connectionById(connectionId);
    if (!c) return;
    this.svc.testConnection({ connectionId: c.connectionId, kind: c.kind, endpoint: c.endpoint, apiKey: null, timeoutSeconds: c.timeoutSeconds }).subscribe({
      next: (r) => {
        if (r.ok) this.modelsByConnection.update((m) => ({ ...m, [c.connectionId]: r.models }));
        (r.ok ? toast.success : toast.error)(r.message);
      },
      error: (e) => toast.error(problemMessage(e)),
    });
  }

  // ── Central switches ───────────────────────────────────────

  readonly savingPolicy = signal(false);
  readonly off = OFF;

  /** Turning cloud AI on or all AI off is asked for first; the other direction (safer) is not. */
  async setPolicy(change: { aiEnabled?: boolean; allowCloud?: boolean }): Promise<void> {
    const current = this.settings()?.policy;
    if (!current || this.savingPolicy()) return;
    const next = { aiEnabled: change.aiEnabled ?? current.aiEnabled, allowCloud: change.allowCloud ?? current.allowCloud };
    const ask = change.allowCloud === true ? 'allowCloud' : change.aiEnabled === false ? 'aiOff' : null;
    if (ask) {
      const ok = await this.confirmDlg.confirm({
        title: this.t('aiSettings.switches.confirm.' + ask + '.title'),
        message: this.t('aiSettings.switches.confirm.' + ask + '.message'),
        confirmLabel: this.t('aiSettings.switches.confirm.' + ask + '.ok'),
        cancelLabel: this.t('actions.cancel'),
        variant: ask === 'allowCloud' ? 'danger' : 'warning',
      });
      if (!ok) {
        this.load();
        return;
      }
    }
    this.savingPolicy.set(true);
    this.svc.savePolicy(next.aiEnabled, next.allowCloud).subscribe({
      next: () => {
        this.savingPolicy.set(false);
        this.svc.refreshStatus();
        toast.success(this.t('aiSettings.switches.saved'));
        this.load();
      },
      error: (e) => {
        this.savingPolicy.set(false);
        toast.error(problemMessage(e));
        this.load();
      },
    });
  }

  // ── Connections ────────────────────────────────────────────

  private blank(): ConnectionForm {
    return { name: '', kind: 'OpenAiCompatible', endpoint: '', apiKey: '', clearApiKey: false, inHouse: true, timeoutSeconds: 120, notes: '', isActive: true };
  }

  openConnection(c: AiConnectionItem | null): void {
    this.editing.set(c);
    this.form.set(
      c
        ? { name: c.name, kind: c.kind, endpoint: c.endpoint, apiKey: '', clearApiKey: false, inHouse: c.inHouse, timeoutSeconds: c.timeoutSeconds, notes: c.notes ?? '', isActive: c.isActive }
        : this.blank(),
    );
    this.testResult.set(null);
    this.fieldErrors.set({});
    this.dialogOpen.set(true);
  }

  patch(p: Partial<ConnectionForm>): void {
    this.form.update((f) => {
      const next = { ...f, ...p };
      if (p.kind === 'Gemini') {
        next.inHouse = false;
        if (!f.endpoint) next.endpoint = ENDPOINTS.Gemini;
      }
      return next;
    });
    this.testResult.set(null);
  }

  test(): void {
    const f = this.form();
    this.testing.set(true);
    this.testResult.set(null);
    this.svc
      .testConnection({ connectionId: this.editing()?.connectionId ?? null, kind: f.kind, endpoint: f.endpoint.trim(), apiKey: f.apiKey.trim() || null, timeoutSeconds: f.timeoutSeconds })
      .subscribe({
        next: (r) => {
          this.testing.set(false);
          this.testResult.set(r);
          const id = this.editing()?.connectionId;
          if (r.ok && id) this.modelsByConnection.update((m) => ({ ...m, [id]: r.models }));
        },
        error: (e) => {
          this.testing.set(false);
          this.testResult.set({ ok: false, message: problemMessage(e), models: [] });
        },
      });
  }

  save(): void {
    const f = this.form();
    const c = this.editing();
    this.saving.set(true);
    this.fieldErrors.set({});
    this.svc
      .saveConnection(c?.connectionId ?? null, {
        rowVer: c?.rowVer ?? null, name: f.name.trim(), kind: f.kind, endpoint: f.endpoint.trim(), apiKey: f.apiKey.trim() || null,
        clearApiKey: f.clearApiKey, inHouse: f.kind !== 'Gemini' && f.inHouse, timeoutSeconds: Number(f.timeoutSeconds) || 120,
        notes: f.notes.trim() || null, isActive: f.isActive,
      })
      .subscribe({
        next: (r) => {
          this.saving.set(false);
          this.dialogOpen.set(false);
          const models = this.testResult()?.models;
          if (models?.length) this.modelsByConnection.update((m) => ({ ...m, [r.connectionId]: models }));
          this.svc.refreshStatus();
          toast.success(this.t('aiSettings.connectionSaved', { name: f.name.trim() }));
          this.load();
        },
        error: (e) => {
          this.saving.set(false);
          const errors = (e?.error?.errors ?? {}) as Record<string, string[]>;
          this.fieldErrors.set(Object.fromEntries(Object.entries(errors).map(([k, v]) => [k.charAt(0).toLowerCase() + k.slice(1), v[0]])));
          toast.error(problemMessage(e));
        },
      });
  }

  async remove(c: AiConnectionItem): Promise<void> {
    const ok = await this.confirmDlg.confirm({
      title: this.t('aiSettings.deleteConnection'),
      message: this.t('aiSettings.deleteMessage', { name: c.name }),
      confirmLabel: this.t('aiSettings.deleteConnection'),
      cancelLabel: this.t('actions.cancel'),
      variant: 'danger',
    });
    if (!ok) return;
    this.svc.deleteConnection(c).subscribe({
      next: () => {
        this.svc.refreshStatus();
        this.load();
      },
      error: (e) => toast.error(problemMessage(e)),
    });
  }

  leavesNetwork = (c: { kind: AiConnectionKind; inHouse: boolean }) => c.kind === 'Gemini' || !c.inHouse;

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate(key, params);
  }
}
