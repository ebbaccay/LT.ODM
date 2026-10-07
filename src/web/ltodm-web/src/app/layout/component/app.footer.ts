import { Component } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

@Component({
  selector: 'app-footer',
  imports: [TranslocoPipe],
  template: `
    <footer class="text-muted-foreground border-border mx-4 border-t py-4 text-center text-xs sm:mx-6">
      {{ 'layout.footer' | transloco }}
    </footer>
  `,
})
export class AppFooter {}
