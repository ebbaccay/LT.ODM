export const environment = {
  production: true,
  // Same-origin by default: serve the Angular files and the API under one IIS site / reverse proxy.
  // If the API lives on another origin, set it here (and add a production CORS policy in the API).
  apiBaseUrl: '',

  // ---- Settings read by modules ported from TMS (src/app/tms) ----
  /** Secured TMS compatibility hub (LT.ODM.Api TmsProcedureHub). */
  spHubUrl: '/hubs/sp',
  /** Notifications hub (TMS garment-quotation notifications). */
  notificationsHubUrl: '/hubs/notifications',
  skipNegotiation: false,
  transport: 'WebSockets',
  spDebug: false,
  logLevel: 'warn',
  /** Prefix the TMS screens put on procedure names; the API ignores it. */
  appDb: 'IPLEX_SRCDB01',
  /** Where TMS images/files are served from. TODO: set when file storage is ported. */
  fileServerUrl: '',
  localeKeyString: 'ltodm.tms',
};
