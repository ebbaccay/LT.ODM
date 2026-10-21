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

**Values with `$` or `` ` ``** (passwords, keys): PowerShell expands them inside double quotes, so IIS silently stores a different value (e.g. a SQL password `Abc$123x!` becomes `Abc!` and the API fails with *Login failed*). Use single quotes, writing each `'` inside as `''`:

```powershell
& $appcmd set config -section:system.applicationHost/applicationPools '/[name=''LTODM''].environmentVariables.[name=''ConnectionStrings__StyleLibrary''].value:Server=SQL01;Database=LTODM;User ID=ltodm_app_login;Password=Abc$123x!;Encrypt=True' /commit:apphost
```

Check what was stored with `& $appcmd list apppool LTODM /config`. Generating passwords from letters and digits only avoids the problem altogether.

Recycle the app pool after changing variables. `applicationHost.config` is readable by administrators only; keep it that way.

## Required

| Setting | Example | Notes |
|---|---|---|
| `ConnectionStrings:StyleLibrary` | `Server=SQL01;Database=LTODM;User ID=ltodm_app_login;Password=...;Encrypt=True` | **Secret** (contains the password). SQL login from [Database §3](02-database.md#3-permissions-for-the-application), on IIS and Linux alike. `TrustServerCertificate=True` only while SQL Server has no trusted certificate (development) |
| `Jwt:SigningKey` | 64+ random characters | Signs sign-in tokens. Generate: `$b = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)` (works in Windows PowerShell 5.1 and PowerShell 7). Different per environment. Changing it signs everyone out |
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

Without SMTP, new users cannot receive their set-password link; an Admin can send a new link once SMTP works, or meanwhile choose **Set a temporary password** when adding the user (or **Edit access > Set temporary password** for an existing one).

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
| `RateLimiting:PermitPerMinute` | 600 | All API requests per **signed-in user** per minute (per client IP before sign-in). Pictures (`.../images/...`) do not count. Too many answers `429 Too Many Requests` until the minute is over |

The overall limit used to be 100 per client IP, which a few quick refreshes of a page with pictures could use up, and which everyone behind one office address (NAT, VPN, remote desktop) shared.

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
| `Ai:OpenAiCompatible:Model`, `ImageModel`, `ImageEndpoint`, `ImageSize` (1024x1024), `ImageSketchStrength` (0.85), `ImageFitShape` (true), `ApiKey`, `TimeoutSeconds` (120), `InHouse` (true) | | `ImageSize` and `ImageSketchStrength` are also used for in-house image connections set in the app. `ImageSketchStrength`: how far a render may move away from the sketch (0-1); **0 = never send the sketch**, for image servers without `images/edits`. `ImageFitShape`: a clearly tall drawing (a dress) is sent on a portrait canvas (`ImageSize` width x 1.5 height, e.g. 256x384), a clearly wide one on a landscape canvas; the image server must allow those sizes (false = always `ImageSize`). On a CPU-only server use `512x512` or smaller |
| `MaterialReader:BatchSize` | 25 | Material descriptions per AI call in Materials > Description reader (1-25). Lower it for a slow AI server: on a CPU-only server one material takes 1-3 minutes, so 25 per call shows nothing for 20+ minutes; **5** shows results every few minutes. Environment variable `MaterialReader__BatchSize`. The page reads it from the API (button *Read next N*, chosen materials sent N at a time) |

See [AI overview](../ai/overview.md).

### Image search look-alike model (built into the API)

Search by photo, by words and "more like this" use an image model that runs **inside the API** (ONNX Runtime on the CPU; no AI server, nothing sent anywhere). Its files are not in the release: copy them to the server once ([File storage](08-file-storage.md#image-search-model-files)).

| Setting | Default | Notes |
|---|---|---|
| `ImageSearch:ImageModel:Folder` | `App_Data/models/marqo-fashionSigLIP` | Relative paths start at the API folder. On servers set it **outside the site**, e.g. `D:\LTODM\models\marqo-fashionSigLIP`, so a release does not remove it. Empty = look-alike search off (photos then go to the Vision job) |
| `ImageSearch:ImageModel:File` | `onnx/vision_model_int8.onnx` | The model's vision part inside Folder |
| `ImageSearch:ImageModel:TextFile` | `onnx/text_model_int8.onnx` | The model's text part (search by words without the AI server). Empty, or the file missing = words go to the descriptions |
| `ImageSearch:ImageModel:TokenizerFile` | `tokenizer.json` | The text part's tokenizer (Hugging Face format, SentencePiece Unigram) |
| `ImageSearch:ImageModel:Name` | (folder/file name, `marqo-fashionSigLIP/vision_model_int8`) | Stored with every vector. Changing it, Folder's name or File makes every picture be indexed again (under a second each) |
| `ImageSearch:ImageModel:Size`, `Mean`, `Std` | 224, 0.5, 0.5 | Input size and pixel normalisation of the model. Change only with another model |

The model must return 768 numbers per picture (`style.ImageVector.Vector` is `vector(768)`). The **AI features** switch in Settings > AI connections turns it off too.

Settings > Import pictures uses the same model to choose the garment on a sheet ([Settings](../components/settings.md#import-pictures-settingsimage-import)).

### Picture upscaler (built into the API)

Settings > Import pictures enlarges small cut-outs with an upscaler that runs **inside the API** (ONNX Runtime on the CPU; nothing sent anywhere). Its file is not in the release: copy it to the server once ([File storage](08-file-storage.md#picture-upscaler-file)).

| Setting | Default | Notes |
|---|---|---|
| `StyleImages:Upscaler:File` | `App_Data/models/realesr-general-x4v3/realesr-general-x4v3.onnx` | Relative paths start at the API folder. On servers set it **outside the site**, e.g. `D:\LTODM\models\realesr-general-x4v3\realesr-general-x4v3.onnx`. Empty or missing = small pictures are enlarged smoothly instead |

The model must take an RGB picture (values 0-1, `[1, 3, h, w]`) and return it 4x larger. The **AI features** switch turns it off too.

## Other

| Setting | Notes |
|---|---|
| `AgGrid:LicenseKey` | AG Grid Enterprise licence (secret-ish: it reaches the browser, but keep it out of the repository). Without it grids show a watermark |
| `TmsProcedures` (in `tms-procedures.json`, shipped with the API) | Which TMS procedures each ported module may call, and how parameters are filled from the signed-in user. Part of the release; do not edit on servers |
| `Logging:LogLevel:*` | `Information` by default; `Microsoft.AspNetCore` at `Warning` |
| `ForwardedHeaders:KnownProxies` | Linux behind nginx only, when nginx runs on **another** server: its IP address(es), e.g. `ForwardedHeaders__KnownProxies__0=10.0.0.5`. nginx on the same server (loopback) is trusted without it. See [Linux and nginx](09-linux-nginx.md) |
| `Database:ConnectionName` | Which `ConnectionStrings` entry the API uses (default `StyleLibrary`). Developer PCs only: the `https-devserver` launch profile sets it to `StyleLibraryDevServer`. Leave unset on servers |
| `ASPNETCORE_ENVIRONMENT` | Leave unset on servers (= `Production`). `Development` shows error details and the OpenAPI pages: never on a server |
