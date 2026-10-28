import { Routes } from '@angular/router';
import { authGuard, guestGuard, roleGuard } from './core/auth/auth.guards';
import { agGridGuard } from './core/grid/ag-grid.guard';
import { AuthShell } from './features/auth/auth-shell';
import { AppLayout } from './layout/component/app.layout';
import { ComingSoon } from './pages/coming-soon/coming-soon';
import { Home } from './pages/home/home';
import { TMS_ROUTES } from './tms/tms.routes';

export const routes: Routes = [
  // Style Packages: a package opened by the client through its share link, without signing in (no app layout, no guard).
  { path: 'share/:token', loadComponent: () => import('./features/style-packages/shared-package-page').then((m) => m.SharedPackagePage), title: 'titles.shared' },
  // The app: signed-in users only. Listed first so "/" matches here, not the auth shell.
  {
    path: '',
    component: AppLayout,
    canActivate: [authGuard],
    canActivateChild: [authGuard],
    children: [
      { path: '', component: Home, title: 'titles.dashboard' },
      // Style Library (the API checks the same roles; Admin and Merchandiser can also edit).
      {
        path: 'styles',
        canActivate: [roleGuard('Admin', 'Merchandiser', 'Costing', 'Viewer')],
        children: [
          { path: '', loadComponent: () => import('./features/style-library/styles-list').then((m) => m.StylesList), title: 'titles.styles' },
          // Fill the gaps: suggested genders, product types, season merges and LCO figures to accept (before ':id').
          { path: 'gaps', loadComponent: () => import('./features/style-library/gaps-page').then((m) => m.GapsPage), title: 'titles.fillGaps' },
          {
            path: ':id',
            loadComponent: () => import('./features/style-library/style-detail').then((m) => m.StyleDetail),
            title: 'titles.style',
          },
          // What changed from an earlier style (rules only, no AI needed).
          {
            path: ':id/compare',
            loadComponent: () => import('./features/style-library/style-compare').then((m) => m.StyleComparePage),
            title: 'titles.styleCompare',
          },
        ],
      },
      // Style Packages: library styles offered to a client (same readers; Admin and Merchandiser change them).
      {
        path: 'packages',
        canActivate: [roleGuard('Admin', 'Merchandiser', 'Costing', 'Viewer')],
        children: [
          { path: '', loadComponent: () => import('./features/style-packages/packages-list').then((m) => m.PackagesList), title: 'titles.packages' },
          // What to offer a customer (rules, no AI); before ':id'.
          { path: 'offer', loadComponent: () => import('./features/style-packages/offer-page').then((m) => m.OfferPage), title: 'titles.offer' },
          { path: 'hit-rate', loadComponent: () => import('./features/style-packages/hit-rate-page').then((m) => m.HitRatePage), title: 'titles.hitRate' },
          { path: ':id', loadComponent: () => import('./features/style-packages/package-detail').then((m) => m.PackageDetailPage), title: 'titles.package' },
        ],
      },
      // AI Studio on the Style Library (same readers; the API lets only Admin and Merchandiser make renders).
      {
        path: 'ai',
        canActivate: [roleGuard('Admin', 'Merchandiser', 'Costing', 'Viewer')],
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'search' },
          { path: 'search', loadComponent: () => import('./features/ai-studio/smart-search').then((m) => m.SmartSearch), title: 'titles.aiSearch' },
          // Questions about the library: phrase list first, then the Text job picks one read-only tool.
          { path: 'assistant', loadComponent: () => import('./features/ai-studio/library-assistant').then((m) => m.LibraryAssistant), title: 'titles.aiAssistant' },
          // In-house only: Vision + Embedding jobs; the API lets only Admin and Merchandiser build the index.
          { path: 'image-search', loadComponent: () => import('./features/ai-studio/image-search').then((m) => m.ImageSearch), title: 'titles.aiImageSearch' },
          { path: 'compare', loadComponent: () => import('./features/ai-studio/change-summary').then((m) => m.ChangeSummary), title: 'titles.aiCompare' },
          { path: 'bom-check', loadComponent: () => import('./features/ai-studio/bom-check').then((m) => m.BomCheckPage), title: 'titles.aiBomCheck' },
          { path: 'render', loadComponent: () => import('./features/ai-studio/style-render').then((m) => m.StyleRenderPage), title: 'titles.aiRender' },
          // Planned capabilities with sample results next to live readiness numbers (no AI calls).
          { path: 'lab', loadComponent: () => import('./features/ai-studio/ai-lab').then((m) => m.AiLab), title: 'titles.aiLab' },
        ],
      },
      // Placeholders until the library features are built.
      // The BOM seen by material (the old BOMs placeholder now points here).
      {
        path: 'materials',
        canActivate: [roleGuard('Admin', 'Merchandiser', 'Costing', 'Viewer')],
        children: [
          { path: '', loadComponent: () => import('./features/materials/materials-list').then((m) => m.MaterialsList), title: 'titles.materials' },
          {
            path: ':id',
            loadComponent: () => import('./features/materials/material-detail').then((m) => m.MaterialDetailPage),
            title: 'titles.material',
          },
        ],
      },
      { path: 'boms', pathMatch: 'full', redirectTo: 'materials' },
      { path: 'workmanship', component: ComingSoon, data: { heading: 'titles.workmanship' }, title: 'titles.workmanship' },
      { path: 'costing', component: ComingSoon, data: { heading: 'titles.costing' }, title: 'titles.costing' },
      {
        path: 'account/password',
        loadComponent: () => import('./pages/account/change-password').then((m) => m.ChangePassword),
        title: 'titles.changePassword',
      },
      // Modules ported from TMS, at their TMS paths.
      ...TMS_ROUTES,
      {
        path: 'ui-reference',
        canActivate: [agGridGuard],
        loadComponent: () => import('./pages/ui-reference/ui-reference').then((m) => m.UiReference),
        title: 'titles.uiReference',
      },
    ],
  },
  // Sign-in pages (glass layout, no sidebar).
  {
    path: '',
    component: AuthShell,
    children: [
      { path: 'login', canActivate: [guestGuard], loadComponent: () => import('./features/auth/login').then((m) => m.Login), title: 'titles.signIn' },
      {
        path: 'forgot-password',
        canActivate: [guestGuard],
        loadComponent: () => import('./features/auth/forgot-password').then((m) => m.ForgotPassword),
        title: 'titles.forgotPassword',
      },
      {
        path: 'reset-password',
        loadComponent: () => import('./features/auth/reset-password').then((m) => m.ResetPassword),
        title: 'titles.resetPassword',
      },
    ],
  },

  { path: '', loadChildren: () => import('./pages/status/status.routes') },
  { path: '**', redirectTo: '/notfound' },
];
