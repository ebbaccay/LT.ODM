import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { AuthImgDirective } from '@tms/shared/ui/auth-img';
import { Modal } from '@tms/shared/ui/modal';
import { styleError } from '../style-library/style-library.service';
import { PackageItem, PackagesService } from './packages.service';

/**
 * One style in a package: the note for the client (cards, line sheet), the internal note (LT only) and which colourways are
 * offered (all, or the ones ticked). A chosen colourway no longer on the style shows as missing until it is unticked.
 */
@Component({
  selector: 'app-package-item-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AuthImgDirective, Modal, HlmButtonImports, HlmCheckboxImports, HlmLabelImports, HlmSpinnerImports, HlmTextareaImports, TranslocoPipe],
  template: `
    <app-modal [open]="open()" size="lg" [heading]="'packages.item.title' | transloco: { styleNo: item()?.styleNo ?? '' }" (closed)="closed.emit()">
      @let e = errors();
      <div class="flex flex-col gap-4">
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="pi-pitch">{{ 'packages.item.pitchNote' | transloco }}</label>
          <textarea hlmTextarea id="pi-pitch" rows="3" maxlength="1000" [placeholder]="'packages.item.pitchNotePlaceholder' | transloco"
                    [value]="pitchNote()" (input)="pitchNote.set($any($event.target).value)"></textarea>
          @if (e['pitchNote']) { <p class="text-destructive text-xs">{{ e['pitchNote'] }}</p> }
        </div>
        <div class="flex flex-col gap-1.5">
          <label hlmLabel for="pi-internal">{{ 'packages.item.internalNote' | transloco }}</label>
          <textarea hlmTextarea id="pi-internal" rows="2" maxlength="1000" [placeholder]="'packages.item.internalNotePlaceholder' | transloco"
                    [value]="internalNote()" (input)="internalNote.set($any($event.target).value)"></textarea>
          @if (e['internalNote']) { <p class="text-destructive text-xs">{{ e['internalNote'] }}</p> }
        </div>

        <fieldset class="flex flex-col gap-2">
          <legend class="mb-1 text-sm font-medium">{{ 'packages.item.colorways' | transloco }}</legend>
          <label class="flex min-h-11 items-center gap-2 text-sm lg:min-h-0" for="pi-all">
            <hlm-checkbox inputId="pi-all" [checked]="all()" (checkedChange)="all.set(!all())" />
            {{ 'packages.item.allColorways' | transloco: { count: available().length } }}
          </label>
          @if (!all()) {
            @if (!(item()?.colorways ?? []).length) {
              <p class="text-muted-foreground text-xs">{{ 'packages.item.noColorways' | transloco }}</p>
            }
            <ul class="grid gap-1.5 sm:grid-cols-2" role="list">
              @for (c of item()?.colorways ?? []; track c.colorwayCode) {
                <li>
                  <label class="hover:bg-accent/40 flex min-h-11 items-center gap-2 rounded-md border px-2 py-1.5 text-sm" [for]="'pi-cw-' + $index">
                    <hlm-checkbox [inputId]="'pi-cw-' + $index" [checked]="chosen().has(c.colorwayCode)" (checkedChange)="toggle(c.colorwayCode)" />
                    <span class="bg-muted flex size-8 shrink-0 items-center justify-center overflow-hidden rounded border">
                      @if (c.imageUrl) { <img [appAuthSrc]="c.imageUrl" alt="" class="size-full object-cover" loading="lazy" /> }
                    </span>
                    <span class="min-w-0">
                      <span class="block truncate font-mono text-xs">{{ c.colorwayCode }}</span>
                      @if (c.missing) {
                        <span class="text-destructive block text-xs">{{ 'packages.item.missing' | transloco }}</span>
                      } @else {
                        <span class="text-muted-foreground block truncate text-xs">{{ c.colorwayName }}{{ c.status === 'DROPPED' ? ' · ' + ('packages.item.dropped' | transloco) : '' }}</span>
                      }
                    </span>
                  </label>
                </li>
              }
            </ul>
            @if (e['colorwayCodes']) { <p class="text-destructive text-xs">{{ e['colorwayCodes'] }}</p> }
          }
        </fieldset>
      </div>
      <ng-container footer>
        <button hlmBtn variant="outline" type="button" class="h-11 lg:h-9" (click)="closed.emit()">{{ 'actions.cancel' | transloco }}</button>
        <button hlmBtn type="button" class="h-11 lg:h-9" [disabled]="saving()" (click)="save()">
          @if (saving()) { <hlm-spinner /> }
          {{ 'actions.save' | transloco }}
        </button>
      </ng-container>
    </app-modal>
  `,
})
export class PackageItemDialog {
  private readonly svc = inject(PackagesService);
  private readonly transloco = inject(TranslocoService);

  readonly open = input(false);
  readonly packageId = input(0);
  readonly item = input<PackageItem | null>(null);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly pitchNote = signal('');
  readonly internalNote = signal('');
  readonly all = signal(true);
  readonly chosen = signal<ReadonlySet<string>>(new Set());
  readonly errors = signal<Record<string, string>>({});
  readonly saving = signal(false);
  /** Colourways on the style now (not the missing ones). */
  readonly available = computed(() => (this.item()?.colorways ?? []).filter((c) => !c.missing));

  constructor() {
    effect(() => {
      const i = this.item();
      if (!this.open() || !i) return;
      this.pitchNote.set(i.pitchNote ?? '');
      this.internalNote.set(i.internalNote ?? '');
      this.all.set(i.allColorways);
      // Offering all: start from every colourway on the style ticked, so unticking all shows them ticked.
      this.chosen.set(new Set(i.colorways.filter((c) => (i.allColorways ? !c.missing : c.offered)).map((c) => c.colorwayCode)));
      this.errors.set({});
    });
  }

  toggle(code: string): void {
    this.chosen.update((s) => {
      const next = new Set(s);
      if (!next.delete(code)) next.add(code);
      return next;
    });
  }

  save(): void {
    const i = this.item();
    if (!i || this.saving()) return;
    if (!this.all() && !this.chosen().size) {
      this.errors.set({ colorwayCodes: this.transloco.translate('packages.item.chooseColorway') });
      return;
    }
    this.saving.set(true);
    this.svc
      .saveItem(this.packageId(), i.packageItemId, {
        rowVer: i.rowVer, pitchNote: this.pitchNote().trim() || null, internalNote: this.internalNote().trim() || null,
        allColorways: this.all(), colorwayCodes: this.all() ? [] : [...this.chosen()],
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.saved.emit();
        },
        error: (err) => {
          this.saving.set(false);
          const { message, fields } = styleError(err);
          this.errors.set(fields);
          toast.error(message);
        },
      });
  }
}
