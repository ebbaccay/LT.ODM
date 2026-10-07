import { Component } from '@angular/core';
import { Routes } from '@angular/router';
import { StatusPage } from './status-page';

@Component({
  selector: 'app-notfound',
  imports: [StatusPage],
  template: `<app-status-page code="404" title="status.notFoundTitle" message="status.notFoundMessage" icon="lucideSearchX" />`,
})
export class Notfound {}

@Component({
  selector: 'app-access-denied',
  imports: [StatusPage],
  template: `
    <app-status-page
      code="403"
      title="status.accessDeniedTitle"
      message="status.accessDeniedMessage"
      icon="lucideLock"
      tone="warning"
    />
  `,
})
export class AccessDenied {}

@Component({
  selector: 'app-error',
  imports: [StatusPage],
  template: `
    <app-status-page
      title="status.errorTitle"
      message="status.errorMessage"
      icon="lucideCircleAlert"
      tone="destructive"
    />
  `,
})
export class ErrorPage {}

export default [
  { path: 'notfound', component: Notfound, title: 'titles.notFound' },
  { path: 'access', component: AccessDenied, title: 'titles.accessDenied' },
  { path: 'error', component: ErrorPage, title: 'titles.error' },
] satisfies Routes;
