import { ApplicationConfig, isDevMode, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { TitleStrategy, provideRouter, withInMemoryScrolling } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { provideTransloco } from '@jsverse/transloco';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { TranslatedTitleStrategy } from './core/i18n/translated-title.strategy';
import { TranslationLoader } from './tms/core/translation-loader';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withInMemoryScrolling({ anchorScrolling: 'enabled', scrollPositionRestoration: 'enabled' })),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
    // Page titles are translation keys (titles.*).
    { provide: TitleStrategy, useClass: TranslatedTitleStrategy },
    // UI translations (public/assets/i18n); LanguageService picks the language at start-up.
    provideTransloco({
      config: {
        availableLangs: ['en', 'zh-Hans'],
        defaultLang: 'en',
        reRenderOnLangChange: true,
        // A key missing in zh-Hans shows the English text rather than the key.
        fallbackLang: 'en',
        missingHandler: { useFallbackTranslation: true },
        prodMode: !isDevMode(),
      },
      loader: TranslationLoader,
    }),
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
