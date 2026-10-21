import { HttpInterceptorFn } from '@angular/common/http';

/**
 * API calls that change something or start work (POST, PUT, PATCH, DELETE) go straight to the server, not through the
 * Angular service worker (header ngsw-bypass). Chrome stops a service worker's request after 5 minutes, so slow AI
 * calls on a CPU server (Description reader, renders) failed at 300 s with "Cannot reach the server" while the API was
 * still working (IIS 499, 2026-10-20). Reads (GET) still pass through it, so cached pictures keep working offline.
 */
export const swBypassInterceptor: HttpInterceptorFn = (req, next) =>
  req.method === 'GET' || req.method === 'HEAD' || !/\/(api|hubs)\//.test(req.url)
    ? next(req)
    : next(req.clone({ setHeaders: { 'ngsw-bypass': 'true' } }));
