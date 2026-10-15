# Database

LT ODM uses one SQL Server 2025 database (called `LTODM` below). The API only calls named stored procedures, plus bulk inserts into the import staging tables; there is no generic "run any SQL" endpoint.

## 1. Create the database

```sql
CREATE DATABASE LTODM;
ALTER DATABASE LTODM SET COMPATIBILITY_LEVEL = 170;   -- required (REGEXP_LIKE, native json)
ALTER DATABASE LTODM SET READ_COMMITTED_SNAPSHOT ON;  -- recommended: readers do not block writers
```

## 2. Deploy the scripts

**Easiest: `db\deploy-db.cmd`** (in every release). Double-click it (or run it from a prompt) on a machine with `sqlcmd` that reaches the SQL Server. It asks for the server (default `localhost`), the database (default `LTODM`), a SQL login (blank = Windows sign-in) and its password (hidden, used only for this run), and whether to trust the server's certificate (answer **Y** for a self-signed certificate; newer `sqlcmd` versions refuse it otherwise). It then runs every script below in order and stops at the first error, naming the script. The login needs rights to change the database (e.g. `db_owner`); the website's own login is not enough. For scripted use it reads `LTODM_DB_SERVER`, `LTODM_DB_NAME`, `LTODM_DB_USER`, `SQLCMDPASSWORD`, `LTODM_DB_TRUST` (Y/N) and `LTODM_DB_YES=1` (no questions). The TMS procedures (below) are not part of it.

The same by hand: all scripts are **idempotent**: running them again on an existing database creates what is missing and updates procedures, without touching data. Run them from the `db` folder of the release with `sqlcmd -I` (`-I` turns QUOTED_IDENTIFIER on, which filtered indexes and several procedures need; plain `sqlcmd` defaults to off). `-b` stops on the first error.

```powershell
cd db
$s = "SQLSERVER\INSTANCE"; $d = "LTODM"
function run($f) { sqlcmd -S $s -d $d -E -b -I -i $f; if ($LASTEXITCODE) { throw "Failed: $f" } }

# Sign-in, roles, menu (auth + nav schemas, base roles, default menu)
run deploy-auth.sql
run procedures\usp_Health_Ping.sql

# Style Library (ref, partner, mat, style, staging schemas)
run tables\style.tables.sql
run procedures\style.import.procedures.sql     # Settings > Import (incl. New codes)
run procedures\style.procedures.sql            # Styles screens and home dashboard
run procedures\ref.procedures.sql              # Settings > Reference lists
run procedures\mat.procedures.sql              # Materials and its views
run procedures\mat.spec.procedures.sql         # Materials > Description reader (creates mat.MaterialSpec)
run procedures\style.ai.procedures.sql         # AI Studio: BOM check, renders, concept matching, image search
run procedures\style.tags.procedures.sql       # Picture tags (look filters; tables in tables\style.tables.sql)
run procedures\style.assistant.procedures.sql  # AI Studio > Library assistant (style counts)
run procedures\style.image-import.procedures.sql  # Settings > Import pictures (style matching, set picture, check existing pictures)

# AI connections (ai schema)
run tables\ai.tables.sql
run procedures\ai.procedures.sql
run procedures\ai.assistant.procedures.sql    # Library assistant saved questions (table in tables\ai.tables.sql)

# Menu again: adds pages introduced by the scripts above (keeps changes made in Settings > Menu)
run seed\nav.menu.sql
```

In SSMS instead: enable **Query > SQLCMD Mode** and run the same files in the same order.

### Modules ported from TMS

Garment Quotation, Concept Studio, SBU Submission, Collection Builder, Product Matching, Cost Optimization, Customer Proposal, Market Trends, the Quotation Dashboard and the SBU module still use their **TMS tables and stored procedures** (`dbo.web_rd_*`, `dbo.tms_*`). Deploy them from the TMS database scripts (`TMS/SignalR-UI5-App/database`) into the LT ODM database, then the LT ODM changes to them:

```powershell
run tms\concept-studio.team-visibility.sql     # concept team visibility rules
```

- The API only lets screens call the procedures listed in `tms-procedures.json` (in the API folder). Deploy at least those.
- T-SQL written for SQL Server 2005 runs unchanged on 2025.
- Procedures that reference `IPLEX_SRCDB01` or `GUIWEB` need those databases, or synonyms pointing at their replacements. The 8 Trimcard objects need the linked server `ACT-PROD` (Trimcard is not ported yet).

### One-off fixes

`fixes\style.null-text.sql` repairs data loaded by an early import that stored the text `NULL`. Run it once on a database that had such an import; it is harmless otherwise.

## 3. Permissions for the application

The API signs in with a **SQL login** (SQL authentication), so the same setup works for a web server on Windows (IIS) or Linux (nginx); Windows authentication from Linux would need Kerberos. The SQL Server instance must allow **SQL Server and Windows Authentication mode** (SSMS: server Properties > Security; restart the service after changing it).

The login needs to run procedures and to bulk-insert into the three import staging tables (SELECT and INSERT on those). Nothing else. Use a different password per environment and keep it only in the web server's settings ([Configuration](04-configuration.md)).

```sql
USE master;
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'ltodm_app_login')
    CREATE LOGIN ltodm_app_login WITH PASSWORD = N'<long random password>', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
GO
USE LTODM;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'ltodm_app_login')
    CREATE USER ltodm_app_login FOR LOGIN ltodm_app_login;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'ltodm_app' AND type = 'R')
    CREATE ROLE ltodm_app;
GO
GRANT EXECUTE ON SCHEMA::dbo     TO ltodm_app;   -- TMS procedures, health ping
GRANT EXECUTE ON SCHEMA::auth    TO ltodm_app;
GRANT EXECUTE ON SCHEMA::nav     TO ltodm_app;
GRANT EXECUTE ON SCHEMA::ref     TO ltodm_app;
GRANT EXECUTE ON SCHEMA::style   TO ltodm_app;
GRANT EXECUTE ON SCHEMA::mat     TO ltodm_app;
GRANT EXECUTE ON SCHEMA::staging TO ltodm_app;
GRANT EXECUTE ON SCHEMA::ai      TO ltodm_app;
-- Settings > Import bulk-copies the workbook into these tables (SqlBulkCopy with CheckConstraints).
-- SqlBulkCopy reads each table's columns first with a SELECT, so it needs SELECT as well as INSERT.
GRANT SELECT, INSERT ON staging.StyleRow   TO ltodm_app;
GRANT SELECT, INSERT ON staging.ArticleRow TO ltodm_app;
GRANT SELECT, INSERT ON staging.BomRow     TO ltodm_app;

ALTER ROLE ltodm_app ADD MEMBER ltodm_app_login;
```

`CHECK_EXPIRATION = OFF`: an expiring password would stop the site without warning; rotate it on your own schedule instead (change the login's password, then the connection string, then restart the app). The script can be run again safely.

Connection string: `Server=SQL01;Database=LTODM;User ID=ltodm_app_login;Password=...;Encrypt=True`.

Procedures and tables share an owner (`dbo`), so ownership chaining covers the reads and writes inside them. Test a full import and a commit with this role on a test server before go-live; if a TMS procedure uses dynamic SQL it may need extra grants on the tables it touches.

## 4. Schemas at a glance

| Schema | Holds |
|---|---|
| `auth` | Users, roles, user roles, password history, refresh and reset tokens, login audit |
| `nav` | Sidebar groups, pages and which roles see them |
| `ref` | Pick lists: seasons, season terms, business units, product types, weave and material types, content classes, units |
| `partner` | Customers and suppliers |
| `mat` | Material master, material readings (`MaterialSpec`) |
| `style` | Styles, colorways, BOM lines, colours per colorway, style history, AI renders |
| `staging` | Workbook imports waiting to be committed |
| `ai` | AI connections (API keys encrypted), which connection each AI job uses (or Off), Library assistant saved questions, and the central switches (`ai.Policy`: AI on/off, cloud allowed; cloud starts not allowed on a new database) |
| `dbo` | Tables and procedures from TMS used by the ported modules |

## 5. Health check

`GET https://<site>/health` (no sign-in needed) returns JSON such as `{"status":"Healthy","checks":[{"name":"sqlserver","status":"Healthy"}]}` when the API runs and can reach SQL Server (it calls `dbo.usp_Health_Ping`); the HTTP status is 503 when unhealthy. Use it for monitoring and after each deployment.
