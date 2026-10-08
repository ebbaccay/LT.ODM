# Backup, updates and monitoring

## What to back up

| What | Where | Why | How often |
|---|---|---|---|
| Database `LTODM` | SQL Server | Everything except files | Full daily + log backups per your SQL policy |
| Uploads | `Files:Root`, e.g. `D:\LTODM\uploads` ([File storage](08-file-storage.md)) | Style sketches and photos, AI renders, concept and SBU images (the database only stores their addresses) | Daily, with the database |
| Data-protection key ring | `DataProtection:KeysFolder`, e.g. `D:\LTODM\keys` | Decrypts AI API keys saved in Settings > AI connections | After it changes (rarely); keep with the database backup, access-restricted |
| Server settings | App-pool environment variables, edited `web.config` | Rebuilding the server | When they change; store secrets in your password vault, not in the backup share |

Restoring the database without the uploads leaves styles with broken images; restoring without the key ring means re-typing the AI API keys (everything else works).

## Releasing a new version

1. **Build** on the build machine with `tools\build-release.ps1` ([IIS deployment §1](03-iis-deployment.md#1-build-a-release-build-machine)); it runs the tests and puts the web app into the API's `wwwroot`.
2. **Database first**: run the release's SQL scripts in the documented order ([Database §2](02-database.md#2-deploy-the-scripts)). They are idempotent and only add or change what is new; existing procedures keep working until the new API starts.
3. **Stop the site**: put `app_offline.htm` in `D:\LTODM\api` (IIS stops the app and shows that page), or stop the app pool.
4. **Copy** the release's `api\` folder over `D:\LTODM\api` (first delete the old `wwwroot`, so old bundles do not pile up). **Put your `web.config` back**, or merge its changes (request limit, environment variables, stdout path). `uploads` and `keys` live outside the site folder and are not touched.
5. **Start**: delete `app_offline.htm` / start the pool. Check `/health`, sign in, open a few pages.
6. Users with the app open see **"New version available"** and can reload; the service worker fetches the new files.

Roll back: copy the previous release back. Database changes are additive, so the previous API keeps working with the updated database.

## Monitoring

- **Health**: poll `https://<site>/health` (anonymous; 200 + `"Healthy"` or 503).
- **Logs**: warnings and errors go to the Windows Application event log; start-up failures appear under the source *IIS AspNetCore Module V2*. For start-up problems set `stdoutLogEnabled="true"` in `web.config` temporarily (logs to `D:\LTODM\logs`), then turn it off again.
- **Sign-in audit**: every sign-in, failure, lockout, password change and role change is in `auth.LoginAudit` (user, IP, time, outcome).
- **AI usage**: AI failures are logged with the provider and HTTP status only (never the prompt). Rate-limited requests get HTTP 429. Gemini usage and cost are in the Google AI Studio / Cloud console for the key.

## Housekeeping

- Staged imports left for a day are cancelled automatically.
- Uploaded images are not deleted when removed from a style or concept (another record may still use them). Clean-up of unused files is not automated yet.
