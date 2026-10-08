# Configuration

The API reads settings from, in order (later wins):

1. `appsettings.json` (in the release; no secrets, defaults only)
2. `appsettings.{Environment}.json` (`Production` on servers; `Development` only on developer PCs)
3. **Environment variables** (servers) or user secrets (developer PCs)

Environment variables use `__` for each level: `Ai:Gemini:ApiKey` becomes `Ai__Gemini__ApiKey`.

## Where to put values on the server

| Kind | Where | Why |
|---|---|---|
| Secrets (signing key, SMTP password, AG Grid key, Gemini key) | **Environment variables of the app pool**, set with `appcmd` (below) | Not in any file under the site folder; not overwritten by a new release |
| Non-secret per-server values (paths, URLs) | App pool environment variables, or `<environmentVariables>` in the API's `web.config` | Either works; `web.config` is replaced by each publish, so app-pool variables are easier to keep |
| AI services used by the app | **In the app: Settings > AI connections** | Changed without a restart; API keys stored encrypted |

Set an app-pool environment variable (IIS 10, run as administrator):

```powershell
$appcmd = "$env:windir\system32\inetsrv\appcmd.exe"
& $appcmd set config -section:system.applicationHost/applicationPools `
  /+"[name='LTODM'].environmentVariables.[name='Jwt__SigningKey',value='PASTE_KEY']" /commit:apphost
# change an existing one: replace /+ with /"[name='LTODM'].environmentVariables.[name='Jwt__SigningKey'].value:NEW"
```

Recycle the app pool after changing variables. `applicationHost.config` is readable by administrators only; keep it that way.

## Required

| Setting | Example | Notes |
|---|---|---|
| `ConnectionStrings:StyleLibrary` | `Server=SQL01;Database=LTODM;Integrated Security=True;Encrypt=True` | Windows authentication as the app-pool account. `TrustServerCertificate=True` is for development only |
| `Jwt:SigningKey` | 64+ random characters | Signs sign-in tokens. Generate: `[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))`. Different per environment. Changing it signs everyone out |
| `Auth:PublicBaseUrl` | `https://ltodm.lt.local` | The address in invitation and password-reset emails |

## Email (invitations, password resets)

| Setting | Default | Notes |
|---|---|---|
| `Email:Host` | (empty) | SMTP relay / Exchange. Empty = emails are not sent (in development they are written to the log) |
| `Email:Port` | 25 | |
| `Email:EnableSsl` | true | |
| `Email:FromAddress` | (empty) | e.g. `ltodm@lt.com` |
| `Email:FromName` | LT ODM Style Library | |
| `Email:UserName`, `Email:Password` | (empty) | Only if the relay needs a login (secret) |

Without SMTP, new users cannot receive their set-password link; an Admin can send a new link once SMTP works.

## Files

| Setting | Default | Notes |
|---|---|---|
| `Files:Root` | `<API folder>\App_Data\uploads` | Style sketches and photos, AI renders (`style-library`), concept images (`concept-inspiration`), SBU product photos (`sbu-products`). Set to a folder outside the site, e.g. `D:\LTODM\uploads`. **Back up** |
| `Translations:OverridesFolder` | `<API folder>\App_Data\i18n` | Corrections made in Settings > Translations (`overrides.json`) and their history (`history\`). Set to a folder outside the site, e.g. `D:\LTODM\i18n`, so a release does not wipe them. App-pool account needs **Modify**. **Back up** |
| `Translations:BaseFolder` | `<API folder>\wwwrootssets\i18n` | The translation files that come with the release (the base the corrections are layered on). Leave empty on servers. Development points it at `src/web/ltodm-web/public/assets/i18n` |
| `Translations:HistoryCount` | 30 | Earlier versions of the corrections kept for Restore |
| `DataProtection:KeysFolder` | `<API folder>\App_Data\keys` | Key ring that encrypts API keys saved in Settings > AI connections (protected with Windows DPAPI for the machine). **Back up with the database**: without it, saved API keys cannot be read and must be typed again. Moving to a new server = copy the folder; the keys are machine-protected, so on a new machine re-enter the AI API keys |

## Sign-in and security

| Setting | Default | |
|---|---|---|
| `Jwt:Issuer`, `Jwt:Audience` | `LT.ODM`, `LT.ODM.Web` | Leave as is |
| `Auth:AccessTokenMinutes` | 15 | Role changes reach a signed-in user within this time |
| `Auth:SessionHours` | 12 | Without "Keep me signed in" |
| `Auth:PersistentSessionDays` | 14 | With "Keep me signed in" |
| `Auth:MaxFailedAttempts` / `Auth:LockoutMinutes` | 5 / 15 | Account lockout |
| `Auth:ResetTokenMinutes` | 30 | Password-reset link lifetime |
| `Auth:ResetRequestsPerHour` | 3 | Per user |
| `Auth:InviteTokenHours` | 72 | New-user set-password link lifetime |
| `Auth:Password:*` | MinLength 12, MaxLength 128, upper, lower, digit, symbol, HistoryCount 5 | Password policy |
| `RateLimiting:AuthPermitPerMinute` | 10 | Sign-in and password requests per client IP per minute |
| `RateLimiting:AiPermitPerMinute` | 10 | AI requests per user per minute (protects cost and rate limits) |

The API also limits every client IP to 100 requests per minute overall.

## AI

The AI service for each job is normally chosen **in the app** (Settings > AI connections). These settings are the fallback for a job left on "App default". The switches in Settings > AI connections apply to them too: with **Allow cloud AI services** off, a Gemini fallback is blocked.

| Setting | Default | Notes |
|---|---|---|
| `Ai:Provider` | `Gemini` | Text job fallback: `Gemini` or `OpenAiCompatible` |
| `Ai:ImageProvider` | `Gemini` | Image job fallback |
| `Ai:Gemini:ApiKey` | (empty) | Secret. Empty = Gemini fallback not set up |
| `Ai:Gemini:Model` / `ImageModel` | `gemini-2.5-flash` / `gemini-2.5-flash-image` | |
| `Ai:Gemini:Endpoint` | `https://generativelanguage.googleapis.com/v1beta` | |
| `Ai:Gemini:TimeoutSeconds` | 60 | |
| `Ai:OpenAiCompatible:Endpoint` | (empty) | e.g. `http://ai-server.lt.local:8000/v1` |
| `Ai:OpenAiCompatible:Model`, `ImageModel`, `ImageEndpoint`, `ImageSize` (1024x1024), `ApiKey`, `TimeoutSeconds` (120), `InHouse` (true) | | `ImageSize` is also used for in-house image connections set in the app |

See [AI overview](../ai/overview.md).

## Other

| Setting | Notes |
|---|---|
| `AgGrid:LicenseKey` | AG Grid Enterprise licence (secret-ish: it reaches the browser, but keep it out of the repository). Without it grids show a watermark |
| `TmsProcedures` (in `tms-procedures.json`, shipped with the API) | Which TMS procedures each ported module may call, and how parameters are filled from the signed-in user. Part of the release; do not edit on servers |
| `Logging:LogLevel:*` | `Information` by default; `Microsoft.AspNetCore` at `Warning` |
| `ASPNETCORE_ENVIRONMENT` | Leave unset on servers (= `Production`). `Development` shows error details and the OpenAPI pages: never on a server |
