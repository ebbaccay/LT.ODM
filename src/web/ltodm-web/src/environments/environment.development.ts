export const environment = {
  production: false,
  // Empty = relative URLs, forwarded to the API by proxy.conf.json during `ng serve`.
  apiBaseUrl: '',

  // ---- Settings read by modules ported from TMS (src/app/tms) ----
  /** Secured TMS compatibility hub (LT.ODM.Api TmsProcedureHub). */
  spHubUrl: '/hubs/sp',
  /** Notifications hub (TMS garment-quotation notifications). */
  notificationsHubUrl: '/hubs/notifications',
  skipNegotiation: false,
  transport: 'WebSockets',
  spDebug: false,
  logLevel: 'debug',
  /** Prefix the TMS screens put on procedure names; the API ignores it. */
  appDb: 'IPLEX_SRCDB01',
  /** Where TMS images/files are served from. TODO: set when file storage is ported. */
  fileServerUrl: '',
  localeKeyString: 'ltodm.tms',
};
