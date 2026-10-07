import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { ClientConfigService } from '../config/client-config.service';

/** Add to any route that shows an AG Grid: loads and licenses AG Grid before the page renders. */
export const agGridGuard: CanActivateFn = async () => {
  const clientConfig = inject(ClientConfigService);
  const { setUpAgGrid } = await import('./ag-grid.setup');
  await setUpAgGrid(clientConfig);
  return true;
};
