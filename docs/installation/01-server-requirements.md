# Server requirements

## Servers

| Role | Recommended | Notes |
|---|---|---|
| Web server | Windows Server 2022 or 2025 **or** Ubuntu Server 24.04 LTS (x64); 4 vCPU, 8 GB RAM (image search's built-in model adds about 0.5 GB once used), 50 GB disk + space for uploads | Windows: IIS, one site serves the API and the web app. Ubuntu: the API runs as a systemd service behind nginx ([Linux and nginx](09-linux-nginx.md)). The same release runs on both |
| Database server | SQL Server **2025** (Standard or Enterprise, on Windows or Linux), database compatibility level **170**, **mixed-mode authentication** | Level 170 is required: the sign-in schema uses `REGEXP_LIKE` and the native `json` type. The API signs in with a SQL login |
| Build machine (can be a developer PC) | .NET 10 SDK, Node.js 22 LTS, Angular CLI 22 | Only needed to build releases; not installed on the web server |
| In-house AI server (later) | GPU server; see [In-house AI server](../ai/in-house-server.md) | Optional; until then AI uses Google Gemini (cloud) |

Uploads (style sketches and photos, concept inspiration, SBU product photos, AI renders) are stored as files under one folder ([File storage](08-file-storage.md)). Plan disk space for them: a few hundred KB to a few MB per image.

## Software on the web server

On Ubuntu: `aspnetcore-runtime-10.0` and nginx; see [Linux and nginx](09-linux-nginx.md). On Windows:

1. **IIS** with these role services:
   - Web Server > Common HTTP Features: Default Document, Static Content, HTTP Errors
   - Application Development: **WebSocket Protocol** (required: the app uses SignalR for the TMS procedure hub and notifications)
   - Security: Request Filtering
   - Health and Diagnostics: HTTP Logging
2. **ASP.NET Core 10 Hosting Bundle** (installs the .NET runtime and the ASP.NET Core Module for IIS). Run `iisreset` after installing it.
3. Nothing else: the API serves the web app itself, so URL Rewrite and ARR are not needed.

## Accounts

| Account | Purpose | Rights |
|---|---|---|
| Service identity: the app-pool identity (`IIS APPPOOL\LTODM`) on IIS, or a local system user `ltodm` on Linux | Runs the API | Modify on the uploads, key-ring and translations folders; no admin / sudo rights |
| SQL login `ltodm_app_login` | The API's database sign-in | Member of the `ltodm_app` role only (see [Database](02-database.md#3-permissions-for-the-application)) |
| SQL deployment account | Runs the SQL scripts at install and on updates | `db_owner` on the LT ODM database (deployment only) |

The API connects to SQL Server with a **SQL login**, so it works the same from IIS and from Linux. Its password lives only in the web server's settings: an app-pool environment variable on IIS (readable by administrators only), a root-only environment file on Linux. Never in a file inside the site folder or the repository.

## Certificates and names

- A DNS name for the app, e.g. `ltodm.lt.local`, and a **TLS certificate** for it from the company CA. HTTPS is required: sign-in cookies are `Secure`, and the installable app (service worker) only works over HTTPS.
- SQL Server should present a trusted certificate so the connection string can use `Encrypt=True` without `TrustServerCertificate`.

## Network and firewall

| From | To | Port | Why |
|---|---|---|---|
| Users' browsers | Web server | 443 | The app |
| Web server | SQL Server | 1433 (or the instance port) | Database |
| Web server | SMTP relay / Exchange | 25 or 587 | Invitation and password-reset emails |
| Web server | `generativelanguage.googleapis.com` | 443 | **Only while an AI job uses Gemini (cloud).** Block it once every AI job points at the in-house server, to make sure no data leaves LT |
| Web server | In-house AI server | e.g. 8000 (vLLM), 11434 (Ollama) | When the in-house AI server is in use |

The web server needs no other outbound internet access. The browser loads nothing from outside the company: fonts, icons, AG Grid and all libraries are bundled with the app.

## Licences

- **AG Grid Enterprise**: the licence key is set in configuration (`AgGrid:LicenseKey`). The current key covers AG Grid versions released before 3 September 2027. Without it the grids work but show a watermark.
- **SkiaSharp** (MIT, free): image compression on the server ([File storage](08-file-storage.md#compression)). Its native `libSkiaSharp.dll` ships with the release under `runtimes\win-x64\native`; nothing to install.
- **Gemini**: an API key from Google AI Studio. A free-tier key lets Google use the prompts to improve its models and has low rate limits; use a paid key or the in-house server for customer data. See [Data sent to AI services](../ai/data-sent.md).
