# First start

## 1. Create the first administrator

Every other user is added in the app; the first Admin is created from the command line on the web server. The command uses the same settings as the site, so give the PowerShell session the required values, and run it as an account with rights on the database (e.g. the deployment account):

```powershell
cd D:\LTODM\api
$env:ConnectionStrings__StyleLibrary = "Server=SQL01;Database=LTODM;Integrated Security=True;Encrypt=True"
$env:Jwt__SigningKey = "<the same key as the site>"
$env:Auth__PublicBaseUrl = "https://ltodm.lt.local"

dotnet .\LT.ODM.Api.dll create-user --username jdoe --email jane.doe@lt.com --name "Jane Doe" --roles Admin
# The password is typed twice without echo and checked against the password policy.
# --must-change   forces a new password at first sign-in
# --group FTY --location F001   for a factory user (normally set in the app)
```

Close the session afterwards so the signing key does not stay in it.

## 2. Sign in and add users

1. Open `https://ltodm.lt.local` and sign in as the Admin.
2. **Settings > User roles > Add user**: name, user name, email, roles, user group, location. The user gets an email with a one-time link (valid 72 hours) to set their own password. Factory users need user group `FTY` and their factory code as location.
3. Check the menu in **Settings > Menu**. The default menu is created by the database scripts; pages without roles are visible to every signed-in user.

## 3. Load the Style Library

1. **Settings > Reference lists**: review customers, seasons, business units, product types and so on. Codes typed in the import or in forms are added automatically; this is where they are cleaned up.
2. **Settings > Import**: download the template, fill it (sheets *Style Header*, *Article*, *BOM Detail*), upload. Check the preview tabs:
   - **Styles**: New / Changed / Unchanged / Blocked per style.
   - **Issues**: errors and warnings by sheet and row.
   - **New codes**: codes the import would add, with suggestions to use an existing code instead (avoids `JACKET` and `JACKETS` both existing).
3. **Commit**. Then open **Styles** and **Materials**.

## 4. Connect the AI service

1. **Settings > AI connections > Add connection**:
   - Type **Gemini (Google cloud)**: the address fills in; paste the API key; **Test connection** should list the models. *Data sent to Gemini leaves LT*: read [Data sent to AI services](../ai/data-sent.md) first and decide whether that is acceptable for your data.
   - Or type **OpenAI-compatible** with the in-house AI server's address (see [In-house AI server](../ai/in-house-server.md)).
2. In **Jobs**, set **Text** (e.g. `gemini-2.5-flash`) and **Image** (e.g. `gemini-2.5-flash-image`), and **Save** each row.
3. **Switches**: a new install starts with **Allow cloud AI services** off, so jobs set to Gemini show "Blocked" and send nothing. Turn it on only once it is agreed that the data may leave LT. An in-house server works with it off.
4. Open **AI Studio > Smart search**: the banner shows which service is used and whether data leaves LT (or, in red, that a switch blocks it).

AI features are optional: without a connection every other part of the app works, and AI pages say that AI is not set up.

## 5. Optional

- **AG Grid licence**: set `AgGrid__LicenseKey` to remove the grid watermark.
- **Install as an app**: users can install LT ODM from the browser (Chrome/Edge: install icon in the address bar; iPhone: Share > Add to Home Screen).
