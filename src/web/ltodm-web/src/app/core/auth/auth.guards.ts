import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

const CHANGE_PASSWORD_URL = '/account/password';

/** Signed-in users only; also sends users with a temporary password to the change-password page. */
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!(await auth.ensureSession())) {
    return router.createUrlTree(['/login'], { queryParams: state.url !== '/' ? { returnUrl: state.url } : {} });
  }
  if (auth.user()?.mustChangePassword && !state.url.startsWith(CHANGE_PASSWORD_URL)) {
    return router.createUrlTree([CHANGE_PASSWORD_URL]);
  }
  return true;
};

/** Login / forgot / reset pages: already signed-in users go straight to the app. */
export const guestGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  // inject() only works before the first await.
  const router = inject(Router);
  return (await auth.ensureSession()) ? router.createUrlTree(['/']) : true;
};

/** Users with one of the given LT ODM roles (auth.Roles, e.g. 'Admin'); others see the access-denied page. Use after authGuard. */
export const roleGuard =
  (...roles: string[]): CanActivateFn =>
  async () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    // Waits for the session itself, so it also works on a full page load (refresh-token sign-in).
    if (!(await auth.ensureSession())) return router.createUrlTree(['/login']);
    return auth.user()?.roles.some((r) => roles.includes(r)) ? true : router.createUrlTree(['/access']);
  };
