import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { HlmCardImports } from '@spartan-ng/helm/card';

/** Placeholder for library sections that are not built yet. Route data heading / note are translation keys. */
@Component({
  selector: 'app-coming-soon',
  imports: [HlmCardImports, TranslocoPipe],
  template: `
    <h1 class="mb-6 text-2xl font-semibold tracking-tight">{{ heading | transloco }}</h1>
    <section hlmCard>
      <div hlmCardContent>
        <p class="text-muted-foreground">{{ note | transloco }}</p>
      </div>
    </section>
  `,
})
export class ComingSoon {
  private readonly data = inject(ActivatedRoute).snapshot.data;
  protected readonly heading: string = this.data['heading'] ?? '';
  protected readonly note: string = this.data['note'] ?? 'comingSoon.note';
}
