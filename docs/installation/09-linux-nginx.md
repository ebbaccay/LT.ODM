# Linux and nginx

The same release that runs on IIS runs on Ubuntu Server 24.04 LTS. The API (Kestrel) serves the web app, `/api/v1/...`, `/health` and the SignalR hubs on `127.0.0.1:5000`; nginx in front of it handles HTTPS and passes every request through. The database is on its own server and the API signs in with the SQL login from [Database §3](02-database.md#3-permissions-for-the-application).

```
browser --443/https--> nginx --http--> 127.0.0.1:5000 Kestrel (LT.ODM.Api + web app) --1433--> SQL Server
```

## 1. Software

```bash
sudo apt update
sudo apt install -y aspnetcore-runtime-10.0 nginx
dotnet --list-runtimes        # must list Microsoft.AspNetCore.App 10.x
```

If `aspnetcore-runtime-10.0` is not in Ubuntu's feed yet, add Microsoft's package feed (packages.microsoft.com) first. No SDK or Node.js on the server: releases are built elsewhere with `tools/build-release.ps1` ([IIS deployment §1](03-iis-deployment.md#1-build-a-release-build-machine)). The release's `web.config` is ignored on Linux.

If SQL Server's certificate comes from the company CA, trust that CA on the web server, so `Encrypt=True` works without `TrustServerCertificate`:

```bash
sudo cp company-root-ca.crt /usr/local/share/ca-certificates/
sudo update-ca-certificates
```

## 2. Service user and folders

```bash
sudo useradd --system --no-create-home --shell /usr/sbin/nologin ltodm
sudo mkdir -p /opt/ltodm/api /var/lib/ltodm/uploads /var/lib/ltodm/keys /var/lib/ltodm/i18n /etc/ltodm
sudo chown -R ltodm:ltodm /var/lib/ltodm
sudo chmod 750 /var/lib/ltodm/uploads /var/lib/ltodm/i18n
sudo chmod 700 /var/lib/ltodm/keys
```

| Folder | Setting | Rights |
|---|---|---|
| `/opt/ltodm/api` | the site (release's `api/` folder) | owned by root, readable by `ltodm` |
| `/var/lib/ltodm/uploads` | `Files__Root` | `ltodm` read/write. **Back up** |
| `/var/lib/ltodm/keys` | `DataProtection__KeysFolder` | `ltodm` only (`700`). **Back up with the database, keep private** |
| `/var/lib/ltodm/i18n` | `Translations__OverridesFolder` | `ltodm` read/write. **Back up** |
| `/var/lib/ltodm/models/marqo-fashionSigLIP` | `ImageSearch__ImageModel__Folder` | `ltodm` read only. Image search model ([File storage](08-file-storage.md#image-search-model-files)) |

**The key folder on Linux.** On Windows the key ring is encrypted with DPAPI; Linux has no DPAPI, so the key files are stored unencrypted and only the folder rights protect them. That is why the folder is `700` and owned by `ltodm`, and why its backups must be access-restricted. At start-up the API logs a warning *"No XML encryptor configured. Key ... may be persisted to storage in unencrypted form"*: expected with this setup. Anyone who can read the folder can decrypt the AI API keys saved in Settings > AI connections.

## 3. Copy the release

From the build machine (e.g. with `scp` or WinSCP) copy the release's `api/` folder to the server, then:

```bash
sudo rsync -a --delete ~/ltodm-2026-10-07/api/ /opt/ltodm/api/
sudo chown -R root:root /opt/ltodm/api
sudo chmod -R a+rX /opt/ltodm/api
```

## 4. Settings

One file, readable by root only (systemd reads it before switching to `ltodm`). **Quote every value**: the connection string contains `;`.

```bash
sudo nano /etc/ltodm/ltodm.env
sudo chmod 600 /etc/ltodm/ltodm.env
```

```ini
ASPNETCORE_URLS="http://127.0.0.1:5000"
ConnectionStrings__StyleLibrary="Server=SQL01;Database=LTODM;User ID=ltodm_app_login;Password=...;Encrypt=True"
Jwt__SigningKey="<64+ random characters: openssl rand -base64 48>"
Auth__PublicBaseUrl="https://ltodm.lt.local"
Files__Root="/var/lib/ltodm/uploads"
DataProtection__KeysFolder="/var/lib/ltodm/keys"
Translations__OverridesFolder="/var/lib/ltodm/i18n"
ImageSearch__ImageModel__Folder="/var/lib/ltodm/models/marqo-fashionSigLIP"
Email__Host="smtp.lt.local"
Email__FromAddress="ltodm@lt.com"
AgGrid__LicenseKey="..."
```

Avoid `"`, `\` and `$` in the SQL password, so the file reads the same in systemd and in a shell. Every setting is described in [Configuration](04-configuration.md). Leave `ASPNETCORE_ENVIRONMENT` unset (= Production).

## 5. systemd service

`/etc/systemd/system/ltodm.service`:

```ini
[Unit]
Description=LT ODM (API and web app)
After=network-online.target
Wants=network-online.target

[Service]
User=ltodm
Group=ltodm
WorkingDirectory=/opt/ltodm/api
ExecStart=/usr/bin/dotnet /opt/ltodm/api/LT.ODM.Api.dll
EnvironmentFile=/etc/ltodm/ltodm.env
Environment=DOTNET_NOLOGO=1
Restart=always
RestartSec=10
KillSignal=SIGINT
TimeoutStopSec=30
SyslogIdentifier=ltodm

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now ltodm
systemctl status ltodm
curl http://127.0.0.1:5000/health          # {"status":"Healthy",...}
```

Run **one** instance only: the AI settings cache, the rate limiter and SignalR live in the process. If it does not start, the reason is in `journalctl -u ltodm -n 100` (e.g. a missing connection string or signing key).

## 6. nginx

`/etc/nginx/sites-available/ltodm`:

```nginx
# WebSockets (SignalR /hubs): pass the Upgrade header through.
map $http_upgrade $connection_upgrade {
    default upgrade;
    ''      close;
}

server {
    listen 80;
    server_name ltodm.lt.local;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl;
    http2 on;
    server_name ltodm.lt.local;

    ssl_certificate     /etc/ssl/ltodm/ltodm.crt;     # company CA certificate, full chain
    ssl_certificate_key /etc/ssl/ltodm/ltodm.key;
    ssl_protocols TLSv1.2 TLSv1.3;

    # Settings > Import accepts workbooks up to 50 MB (nginx default: 1 MB).
    client_max_body_size 55m;

    location / {
        proxy_pass         http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header   Host              $host;
        proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_set_header   Upgrade           $http_upgrade;
        proxy_set_header   Connection        $connection_upgrade;
        # AI calls can take up to 2 minutes (nginx default: 60 s).
        proxy_read_timeout 300s;
        proxy_send_timeout 300s;
        proxy_buffering    off;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/ltodm /etc/nginx/sites-enabled/
sudo rm /etc/nginx/sites-enabled/default
sudo nginx -t && sudo systemctl reload nginx
sudo ufw allow 'Nginx Full'          # if ufw is on
```

The API trusts `X-Forwarded-For` / `X-Forwarded-Proto` from loopback, so with nginx on the same server nothing else is needed: HTTPS redirection, the per-IP rate limits and the sign-in audit see the real client. If nginx runs on another server, set `ForwardedHeaders__KnownProxies__0="<nginx IP>"` in the settings file and change `ASPNETCORE_URLS` to listen on that interface (firewalled to the nginx server only).

## 7. First start and checks

Create the first administrator with the same settings as the service ([First start](05-first-start.md)):

```bash
sudo bash -c 'set -a; . /etc/ltodm/ltodm.env; set +a; cd /opt/ltodm/api; \
  dotnet LT.ODM.Api.dll create-user --username jdoe --email jane.doe@lt.com --name "Jane Doe" --roles Admin'
```

Then run the checks in [IIS deployment §7](03-iis-deployment.md#7-check-the-installation) against `https://ltodm.lt.local`. Garment Quotation spinning forever means the WebSocket headers are missing in nginx; an import failing with *413* means `client_max_body_size`.

## 8. Releasing a new version

```bash
sudo systemctl stop ltodm
sudo rsync -a --delete ~/ltodm-<new>/api/ /opt/ltodm/api/
sudo chown -R root:root /opt/ltodm/api && sudo chmod -R a+rX /opt/ltodm/api
sudo systemctl start ltodm
```

Run the release's `db` scripts first, as on Windows ([Backup, updates and monitoring](06-operations.md)). Settings, uploads, keys and translation corrections live outside `/opt/ltodm/api` and are not touched. Logs: `journalctl -u ltodm`.
