import { Component, inject, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLanguages } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { AVAILABLE_LANGS, LanguageService } from '@services/language.service';

/** Language menu for the top bar and the sign-in pages. Language names are shown in their own language. */
@Component({
  selector: 'app-language-switcher',
  imports: [NgIcon, HlmButtonImports, HlmDropdownMenuImports, HlmTooltipImports, TranslocoPipe],
  providers: [provideIcons({ lucideLanguages })],
  template: `
    <button
      hlmBtn
      variant="ghost"
      size="icon"
      [class]="buttonClass()"
      [attr.aria-label]="'lang.switch' | transloco"
      [hlmTooltip]="'lang.select' | transloco"
      [hlmDropdownMenuTrigger]="menu"
      align="end"
    >
      <ng-icon name="lucideLanguages" class="text-lg" />
    </button>
    <ng-template #menu>
      <hlm-dropdown-menu class="w-44">
        <hlm-dropdown-menu-label>{{ 'lang.select' | transloco }}</hlm-dropdown-menu-label>
        <hlm-dropdown-menu-separator />
        @for (l of langs; track l.code) {
          <button hlmDropdownMenuRadio [checked]="language.currentLang() === l.code" [keepOpen]="false" [attr.lang]="l.code" (triggered)="language.setLanguage(l.code)">
            {{ l.label }}
            <hlm-dropdown-menu-radio-indicator />
          </button>
        }
      </hlm-dropdown-menu>
    </ng-template>
  `,
})
export class LanguageSwitcher {
  protected readonly language = inject(LanguageService);
  protected readonly langs = AVAILABLE_LANGS;
  readonly buttonClass = input('size-11 lg:size-9');
}
