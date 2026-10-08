# Authentication and users

## Signing in

- Sign in with user name or email and password. **Keep me signed in** keeps the session for 14 days; otherwise 12 hours or until the browser closes.
- **Forgot password** emails a one-time link valid for 30 minutes (at most 3 requests per hour). The page answers the same whether or not the email exists.
- After 5 wrong passwords the account is locked for 15 minutes.
- Changing or resetting a password signs the user out on every device.
- The language button (top bar and sign-in pages) switches between English and Simplified Chinese; the choice is kept per browser.

## Password rules

At least 12 characters with upper case, lower case, a number and a symbol; no common passwords or look-alikes (`P@ssw0rd2026!`), no keyboard or number sequences, nothing from the user's name or email, and none of the last 5 passwords.

## New users

An Admin adds users in **Settings > User roles > Add user**: name, user name, email, roles, user group, location. No password is set by the Admin: the user receives an email with a link (valid 72 hours) to choose their own. Until then the account cannot sign in. **Send password link** (envelope icon) sends a fresh link; older links stop working.

Factory users have user group `FTY` and their factory code as location. The system uses both to show them only their own factory's data.

## Roles

| Role | Purpose |
|---|---|
| `Admin` | Everything, including Settings. Cannot remove their own Admin role |
| `Merchandiser` | Office work: Style Library editing, AI Studio including renders, Manage Offerings, SBU module |
| `Costing` | Read the Style Library and AI Studio |
| `Viewer` | Read-only office user |
| `Factory` | Garment Quotation and offers for their own factory |

Role, user group and location changes reach a signed-in user within 15 minutes (at their next token refresh).

## Security notes (for IT)

- Passwords are hashed (PBKDF2-HMAC-SHA512, 100,000 iterations, per-user salt). Refresh and reset tokens are stored only as SHA-256 hashes.
- Access tokens last 15 minutes and live in the app's memory; the refresh token is an HttpOnly, Secure, SameSite=Strict cookie limited to `/api/v1/auth`. It rotates on every use; replaying an old one revokes that sign-in everywhere.
- Every sign-in, failure, lockout, password change, access change and role change is written to `auth.LoginAudit` with the client IP.
- Sign-in and password endpoints: at most 10 requests per minute per IP.
