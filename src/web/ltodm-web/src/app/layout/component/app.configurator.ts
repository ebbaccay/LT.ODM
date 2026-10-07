import { Component, computed, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideMonitor, lucideMoon, lucideSparkles, lucideSquare, lucideSun } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { Accent, LayoutService, MenuMode, SurfacePalette, ThemeMode } from '../service/layout.service';
import { PRIMARY_PALETTES, PrimaryPalette, SURFACE_PALETTES } from '../service/palette';
import { swatch } from '../service/theme-colors';

/** Theme panel for all users. Choices are saved per browser by LayoutService. */
@Component({
  selector: 'app-configurator',
  imports: [NgIcon, HlmButtonImports, TranslocoPipe],
  providers: [provideIcons({ lucideCheck, lucideSun, lucideMoon, lucideMonitor, lucideSparkles, lucideSquare })],
  template: `
    <div class="flex flex-col gap-5">
      <section>
        <h3 class="mb-2 text-sm font-medium">{{ 'theme.mode' | transloco }}</h3>
        <div class="grid grid-cols-3 gap-2">
          @for (m of modes; track m.value) {
            <button hlmBtn size="sm" class="h-11 lg:h-8" [variant]="layout.config().mode === m.value ? 'default' : 'outline'" (click)="layout.update({ mode: m.value })">
              <ng-icon [name]="m.icon" />{{ m.label | transloco }}
            </button>
          }
        </div>
      </section>

      <section>
        <h3 class="mb-2 text-sm font-medium">{{ 'theme.primary' | transloco }}</h3>
        <div class="flex flex-wrap gap-2">
          @for (a of accents(); track a.value) {
            <button
              type="button"
              [class]="swatchClass"
              [class.ring-2]="layout.config().accent === a.value"
              [style.background]="a.swatch"
              [title]="a.label"
              [attr.aria-label]="a.label"
              [attr.aria-pressed]="layout.config().accent === a.value"
              (click)="layout.update({ accent: a.value })"
            ></button>
          }
          <!-- Rainbow swatch until a custom colour is chosen; the colour input covers it so the picker opens beside it. -->
          <label
            [class]="swatchClass + ' relative cursor-pointer'"
            [class.ring-2]="layout.config().accent === 'custom'"
            [style.background]="layout.config().accent === 'custom' ? layout.config().customAccent : rainbow"
            [title]="'theme.custom' | transloco: { colour: layout.config().customAccent }"
          >
            <input
              type="color"
              class="absolute inset-0 size-full cursor-pointer opacity-0"
              [attr.aria-label]="'theme.customColour' | transloco"
              [value]="layout.config().customAccent"
              (click)="layout.update({ accent: 'custom' })"
              (input)="onCustomAccent($event)"
            />
          </label>
        </div>
      </section>

      <section>
        <h3 class="mb-2 text-sm font-medium">{{ 'theme.surface' | transloco }}</h3>
        <div class="flex flex-wrap gap-2">
          @for (s of surfaces; track s.value) {
            <button
              type="button"
              [class]="swatchClass"
              [class.ring-2]="layout.config().surface === s.value"
              [style.background]="s.swatch"
              [title]="s.label"
              [attr.aria-label]="'theme.surfaceLabel' | transloco: { name: s.label }"
              [attr.aria-pressed]="layout.config().surface === s.value"
              (click)="layout.update({ surface: s.value })"
            ></button>
          }
        </div>
      </section>

      <section>
        <h3 class="mb-2 text-sm font-medium">{{ 'theme.glass' | transloco }}</h3>
        <div class="grid grid-cols-2 gap-2">
          @for (g of glassModes; track g.value) {
            <button hlmBtn size="sm" class="h-11 lg:h-8" [variant]="layout.config().glass === g.value ? 'default' : 'outline'" [attr.aria-pressed]="layout.config().glass === g.value" (click)="layout.update({ glass: g.value })">
              <ng-icon [name]="g.icon" />{{ g.label | transloco }}
            </button>
          }
        </div>
      </section>

      <section>
        <h3 class="mb-2 text-sm font-medium">{{ 'theme.radius' | transloco }}</h3>
        <div class="grid grid-cols-5 gap-2">
          @for (r of radii; track r) {
            <button hlmBtn size="sm" class="h-11 lg:h-8" [variant]="layout.config().radius === r ? 'default' : 'outline'" (click)="layout.update({ radius: r })">
              {{ r }}
            </button>
          }
        </div>
      </section>

      <section>
        <h3 class="mb-2 text-sm font-medium">{{ 'theme.menu' | transloco }}</h3>
        <div class="grid grid-cols-2 gap-2">
          @for (m of menuModes; track m.value) {
            <button hlmBtn size="sm" class="h-11 lg:h-8" [variant]="layout.config().menuMode === m.value ? 'default' : 'outline'" (click)="layout.update({ menuMode: m.value })">
              {{ m.label | transloco }}
            </button>
          }
        </div>
      </section>
    </div>
  `,
})
export class AppConfigurator {
  protected readonly layout = inject(LayoutService);

  /** Labels are translation keys. */
  protected readonly modes: { value: ThemeMode; label: string; icon: string }[] = [
    { value: 'light', label: 'theme.light', icon: 'lucideSun' },
    { value: 'dark', label: 'theme.dark', icon: 'lucideMoon' },
    { value: 'system', label: 'theme.system', icon: 'lucideMonitor' },
  ];

  /** Selected swatches get ring-2 on top of this. */
  protected readonly swatchClass = 'size-8 shrink-0 rounded-full ring-primary ring-offset-2 ring-offset-popover transition-transform hover:scale-110 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-ring lg:size-6';
  protected readonly rainbow = 'conic-gradient(#ef4444, #eab308, #22c55e, #06b6d4, #3b82f6, #a855f7, #ef4444)';

  private readonly palettes = (Object.keys(PRIMARY_PALETTES) as PrimaryPalette[]).map((p) => ({
    value: p as Accent,
    label: capitalize(p),
    swatch: swatch(PRIMARY_PALETTES[p][600]),
  }));

  /** Noir follows the chosen surface. */
  protected readonly accents = computed<{ value: Accent; label: string; swatch: string }[]>(() => [
    { value: 'ltodm', label: 'LT Blue', swatch: '#0d47a1' },
    { value: 'noir', label: 'Noir', swatch: swatch(SURFACE_PALETTES[this.layout.config().surface][900]) },
    ...this.palettes,
  ]);

  protected readonly surfaces = (Object.keys(SURFACE_PALETTES) as SurfacePalette[]).map((s) => ({
    value: s,
    label: capitalize(s),
    swatch: swatch(SURFACE_PALETTES[s][500]),
  }));

  protected onCustomAccent(event: Event): void {
    this.layout.update({ accent: 'custom', customAccent: (event.target as HTMLInputElement).value });
  }

  protected readonly glassModes: { value: boolean; label: string; icon: string }[] = [
    { value: true, label: 'theme.glassOn', icon: 'lucideSparkles' },
    { value: false, label: 'theme.glassOff', icon: 'lucideSquare' },
  ];

  protected readonly radii = [0, 0.3, 0.5, 0.625, 1];

  protected readonly menuModes: { value: MenuMode; label: string }[] = [
    { value: 'static', label: 'theme.static' },
    { value: 'overlay', label: 'theme.overlay' },
  ];
}

function capitalize(s: string): string {
  return s.charAt(0).toUpperCase() + s.slice(1);
}
