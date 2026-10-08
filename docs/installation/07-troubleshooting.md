# Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| HTTP 500.30 "ASP.NET Core app failed to start" | Missing required setting (connection string, `Jwt:SigningKey`, `Auth:PublicBaseUrl`), or the .NET 10 Hosting Bundle is missing | Read the event log (source *IIS AspNetCore Module V2*) or enable the stdout log; set the missing value; install the Hosting Bundle and `iisreset` |
| HTTP 500.19 | `web.config` invalid (e.g. a typo while merging it after a release) | Check the XML against [IIS deployment §5](03-iis-deployment.md#5-webconfig) |
| `/health` shows `Unhealthy` | SQL Server unreachable, or the app-pool account has no login / no EXECUTE on `dbo.usp_Health_Ping` | Check the connection string, firewall, and [Database §3](02-database.md#3-permissions-for-the-application) |
| The site shows a 404 or a blank page instead of the app | `wwwroot` missing or empty in `D:\LTODM\api` (the release was built without the web app, or only the API was copied) | Build with `tools\build-release.ps1` and copy the whole `api\` folder |
| Garment Quotation and other ported screens spin forever | WebSockets blocked: IIS WebSocket Protocol feature missing, or a proxy strips the upgrade | Install the feature (and `iisreset`) |
| Users are signed out every few minutes | The app is served on two different host names, or the clock of the server is wrong | Use one host name; check server time |
| Sign-in fails for everyone with "Too many attempts" | A load balancer or proxy was put in front: every user shares its IP, so the per-IP limit (10/min) is shared | Add forwarded-headers handling to the API (see [IIS deployment](03-iis-deployment.md#behind-a-load-balancer-or-reverse-proxy)) |
| Import of a large workbook fails with 404.13 | IIS request limit (30 MB default) | Set `maxAllowedContentLength="57671680"` |
| Images do not show, or upload fails with 500 | App-pool account lacks Modify on `Files:Root` | Grant Modify |
| "New users never get the email" | `Email:*` not set or the relay refuses the server | Set SMTP settings; Admin can resend with **Send password link** |
| AI pages say "AI is not set up" | No connection for the job and no appsettings fallback | Settings > AI connections: add a connection and set the job |
| AI pages say AI is "turned off" or that "cloud AI is not allowed" | A central switch, or the job is set to Off | Settings > AI connections > Switches (or the job's row). Allow cloud only if the data may leave LT; otherwise point the job at the in-house server |
| AI says "The AI service could not answer" | Key refused, model name wrong, quota or rate limit at the provider, or (images) the key has no image access | **Test connection** in Settings > AI connections; check the model name; check the provider's quota |
| AI says "could not be reached" | Firewall blocks the provider (Gemini 443 or the in-house server port) | Open the port from the web server |
| "The API key of AI connection X cannot be decrypted" in the log | Key ring folder lost or the site moved to another server | Re-type the API key in Settings > AI connections |
| HTTP 429 on AI features | Per-user AI limit (10/min) | Wait a minute; raise `RateLimiting:AiPermitPerMinute` if needed |
| Old screens after an update | The service worker still holds the previous version | Users click **Reload** on the "New version available" banner, or reload the page twice. The API already sends `index.html` and `ngsw.json` with `no-cache` |
