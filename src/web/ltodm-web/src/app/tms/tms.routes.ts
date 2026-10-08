import { Routes } from '@angular/router';
import { roleGuard } from '../core/auth/auth.guards';
import { ComingSoon } from '../pages/coming-soon/coming-soon';

/**
 * Screens ported from TMS, at their TMS paths (the menu rows in nav.Items point here).
 * Modules not ported yet show a placeholder; replace each entry with loadComponent as the module lands.
 */
const pending = (path: string, titleKey: string): Routes[number] => ({
  path,
  component: ComingSoon,
  data: { heading: titleKey, note: 'comingSoon.tmsNote' },
  title: titleKey,
});

export const TMS_ROUTES: Routes = [
  {
    path: 'dashboard',
    loadComponent: () => import('./modules/dashboard/dashboard').then((m) => m.Dashboard),
    title: 'titles.quotationDashboard',
  },
  {
    path: 'garment-quotation',
    loadComponent: () => import('./modules/marketing/garment-quotation/garment-quotation').then((m) => m.GarmentQuotation),
    title: 'titles.garmentQuotation',
  },
  {
    path: 'concept-studio',
    loadComponent: () => import('./modules/marketing/concept-studio/concept-studio').then((m) => m.ConceptStudio),
    title: 'titles.conceptStudio',
  },
  {
    path: 'order-management',
    loadComponent: () => import('./modules/marketing/manage-offering/manage-offering').then((m) => m.ManageOffering),
    title: 'titles.sbuSubmission',
  },
  {
    path: 'collection-builder',
    loadComponent: () => import('./modules/marketing/collection-builder/collection-builder').then((m) => m.CollectionBuilder),
    title: 'titles.collectionBuilder',
  },
  {
    path: 'product-catalog',
    loadComponent: () => import('./modules/marketing/product-catalog/product-catalog').then((m) => m.ProductCatalog),
    title: 'titles.productMatching',
  },
  // TMS prototype that only showed mock data; old links go to the real screen.
  { path: 'product-matching/:conceptId', redirectTo: 'product-catalog' },
  {
    path: 'cost-optimization',
    loadComponent: () => import('./modules/marketing/cost-optimization/cost-optimization').then((m) => m.CostOptimization),
    title: 'titles.costOptimization',
  },
  // :id = concept recid (as in TMS)
  {
    path: 'collection/:id',
    loadComponent: () => import('./modules/marketing/collection-summary/collection-summary').then((m) => m.CollectionSummary),
    title: 'titles.customerProposal',
  },
  {
    path: 'collection',
    loadComponent: () => import('./modules/marketing/collection-summary/collection-summary').then((m) => m.CollectionSummary),
    title: 'titles.customerProposal',
  },
  {
    path: 'product-trends',
    loadComponent: () => import('./modules/marketing/product-trends/product-trends').then((m) => m.ProductTrends),
    title: 'titles.marketTrends',
  },
  {
    path: 'sbu-overview',
    loadComponent: () => import('./modules/sbu/sbu-overview/sbu-overview').then((m) => m.SbuOverview),
    title: 'titles.sbuProducts',
  },
  // Every factory's prices: office roles only (the API checks it too).
  {
    path: 'sbu-performance',
    canActivate: [roleGuard('Admin', 'Merchandiser')],
    loadComponent: () => import('./modules/sbu/sbu-performance/sbu-performance').then((m) => m.SbuPerformance),
    title: 'titles.sbuPerformance',
  },
  pending('trimcard', 'titles.trimcard'),
  // Settings: Admin role only (the API checks it too).
  {
    path: 'settings',
    canActivate: [roleGuard('Admin')],
    children: [
      {
        path: 'nav-config',
        loadComponent: () => import('./modules/settings/nav-config/nav-config-editor').then((m) => m.NavConfigEditor),
        title: 'titles.menu',
      },
      { path: 'roles', loadComponent: () => import('./modules/settings/roles/roles').then((m) => m.Roles), title: 'titles.roles' },
      {
        path: 'user-roles',
        loadComponent: () => import('./modules/settings/user-roles/user-roles').then((m) => m.UserRoles),
        title: 'titles.userRoles',
      },
      {
        path: 'import',
        loadComponent: () => import('./modules/settings/style-import/style-import').then((m) => m.StyleImport),
        title: 'titles.import',
      },
      {
        path: 'reference-lists',
        loadComponent: () => import('./modules/settings/reference-lists/reference-lists').then((m) => m.ReferenceLists),
        title: 'titles.refLists',
      },
      {
        path: 'ai',
        loadComponent: () => import('../features/ai-studio/ai-connections').then((m) => m.AiConnections),
        title: 'titles.aiConnections',
      },
      {
        path: 'translations',
        loadComponent: () => import('./modules/settings/translations/translations').then((m) => m.Translations),
        title: 'titles.translations',
      },
      { path: 'content-classes', redirectTo: 'reference-lists?list=contentClasses' },
      { path: '', pathMatch: 'full', redirectTo: 'user-roles' },
    ],
  },
  // TMS change-password screen -> LT ODM account page
  { path: 'change-password', redirectTo: 'account/password' },
];
