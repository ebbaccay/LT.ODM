# IIS deployment

LT ODM runs as **one IIS site**. The API (ASP.NET Core 10) also serves the web app (the Angular build in its `wwwroot` folder), so the app, `/api/v1/...`, `/health` and the SignalR hubs `/hubs/sp` and `/hubs/notifications` share one address, e.g. `https://ltodm.lt.local`.

How the API serves the web app:

- Files in `wwwroot` are served directly. Hashed bundles (`main-X4AI6WBO.js`, `chunk-….js`, `styles-….css`) are cached by browsers for a year; `index.html`, the service-worker files (`ngsw.json`, `ngsw-worker.js`), translations and icons are re-checked on every load, so a new release reaches users at once.
- Any other address without a file extension (`/styles/12`, `/settings/import`) returns `index.html`, so reloads and shared links work.
- Unknown `/api/...` and `/hubs/...` addresses return 404 (after sign-in), never the app page; a missing file returns 404.
- API responses are never cached (`Cache-Control: no-store`).

## 1. Build a release (build machine)

```powershell
cd <repository>
.\tools\build-release.ps1 -Output D:\Releases\ltodm-2026-10-07
```

The script runs the API tests, publishes the API (Release), builds the web app in production mode, copies it into `api\wwwroot` (without source maps), and copies the `db` scripts. Output:

```
D:\Releases\ltodm-2026-10-07\
  api\        the site: LT.ODM.Api.dll, web.config, tms-procedures.json, wwwroot\ (the web app)
  db\         SQL scripts for this release
```

Needs the .NET 10 SDK and Node.js 22 on the build machine. `-SkipTests` skips the tests (not for production releases). `appsettings.Development.json` is removed from the output; it must never be on a server.

## 2. Folders on the web server

```
D:\LTODM\
  api\          the site (the release's api\ folder)
  uploads\      Files:Root - uploaded images (back up; see File storage)
  keys\         DataProtection:KeysFolder - encrypts saved AI API keys (back up, keep private)
  logs\         stdout logs, only when troubleshooting
```

Rights for the app-pool account: **Read** on `api`, **Modify** on `uploads`, `keys` and `logs`. Keep `uploads` and `keys` outside `api`, so a release never overwrites them. See [File storage](08-file-storage.md).

## 3. Application pool

- Name `LTODM`, .NET CLR version **No Managed Code**, pipeline **Integrated**.
- Identity: the domain service account (e.g. `LT\svc-ltodm`).
- Advanced settings: Start mode **AlwaysRunning**, Idle time-out **0**; on the site, Preload enabled. The first user of the day then does not wait for start-up.
- **One worker process** (no web garden): the AI settings cache, the rate limiter and SignalR live in the process.

## 4. Site

1. Copy the release's `api\` folder to `D:\LTODM\api`.
2. IIS Manager > Sites > Add Website: name `LTODM`, physical path `D:\LTODM\api`, app pool `LTODM`.
3. Binding **https**, port 443, host name `ltodm.lt.local`, the company certificate. Add an http binding on 80 only if you want users redirected (the app redirects to HTTPS and sends HSTS).
4. No URL Rewrite or ARR is needed.

## 5. web.config

Edit `D:\LTODM\api\web.config` (keep the `<aspNetCore>` element that `dotnet publish` wrote) and add the request limit and the per-server values:

```xml
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <security>
        <requestFiltering>
          <!-- Settings > Import accepts workbooks up to 50 MB (IIS's default limit is 30 MB). -->
          <requestLimits maxAllowedContentLength="57671680" />
        </requestFiltering>
      </security>
      <aspNetCore processPath="dotnet" arguments=".\LT.ODM.Api.dll" hostingModel="inprocess"
                  stdoutLogEnabled="false" stdoutLogFile="D:\LTODM\logs\stdout">
        <environmentVariables>
          <environmentVariable name="Files__Root" value="D:\LTODM\uploads" />
          <environmentVariable name="DataProtection__KeysFolder" value="D:\LTODM\keys" />
          <environmentVariable name="Auth__PublicBaseUrl" value="https://ltodm.lt.local" />
        </environmentVariables>
      </aspNetCore>
    </system.webServer>
  </location>
</configuration>
```

Each new release brings a fresh `web.config`. Keep your server copy and put it back after copying a release, or set these values as app-pool environment variables instead (they survive releases; see [Configuration](04-configuration.md)).

## 6. Settings and secrets

Set at least these before starting the site (details in [Configuration](04-configuration.md)), as app-pool environment variables:

- `ConnectionStrings__StyleLibrary`
- `Jwt__SigningKey`
- `Auth__PublicBaseUrl`
- `Email__Host`, `Email__Port`, `Email__FromAddress`
- `AgGrid__LicenseKey`
- `Files__Root`, `DataProtection__KeysFolder`

The API **refuses to start** without the connection string, `Jwt:SigningKey` and `Auth:PublicBaseUrl`. The reason is in the Windows Application event log (source *IIS AspNetCore Module V2*) or, with stdout logging on, in `D:\LTODM\logs`.

## 7. Check the installation

1. `https://ltodm.lt.local/health` shows `"status":"Healthy"`.
2. The sign-in page opens, and reloading a deep link (e.g. `https://ltodm.lt.local/styles`) still shows the app.
3. Sign in and open **Garment Quotation**: it loads data through the `/hubs/sp` WebSocket. If it spins forever, the IIS WebSocket Protocol feature is missing.
4. Upload a test image on a style: checks the uploads folder rights.
5. **Settings > Import**: a workbook over 30 MB, if you have one, checks the request limit.
6. In Chrome or Edge, DevTools > Application > Service Workers shows the app's service worker as activated (needs HTTPS).

Then continue with [First start](05-first-start.md).

## Behind a load balancer or reverse proxy

Not needed for a single server. If one is added later, the API must be told to trust the proxy's `X-Forwarded-For` header (a small code change: forwarded-headers middleware with the proxy's address). Without it every user appears to come from the proxy's IP: the per-IP sign-in limit (10 per minute) is then shared by everyone, and the sign-in audit records the proxy's address.
