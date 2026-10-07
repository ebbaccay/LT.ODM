import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';

const API_PREFIX = `${environment.apiBaseUrl}/api/`;

/** Auth endpoints that must not carry the access token or trigger a refresh-and-retry. */
const AUTH_FLOW = /\/api\/v1\/auth\/(login|refresh|logout|forgot-password|reset-password|password-policy)/;

/**
 * Adds the bearer token to API calls. On 401 it refreshes the session once and retries;
 * if that fails the user is sent to the login page.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith(API_PREFIX) || AUTH_FLOW.test(req.url)) {
    return next(req);
  }

  const auth = inject(AuthService);
  const withToken = (r: HttpRequest<unknown>) => {
    const token = auth.getAccessToken();
    return token ? r.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : r;
  };

  return next(withToken(req)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }
      return from(auth.refresh()).pipe(
        switchMap((ok) => {
          if (ok) return next(withToken(req));
          void auth.sessionExpired();
          return throwError(() => error);
        }),
      );
    }),
  );
};
