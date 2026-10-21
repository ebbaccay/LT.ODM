# Authentication and users

## Signing in

- Sign in with user name or email and password. **Keep me signed in** keeps the session for 14 days; otherwise 12 hours or until the browser closes.
- **Forgot password** emails a one-time link valid for 30 minutes (at most 3 requests per hour). The page answers the same whether or not the email exists.
- After 5 wrong passwords the account is locked for 15 minutes.
- Changing or resetting a password signs the user out on every device.
- The language button (top bar and sign-in pages) switches between English and Simplified Chinese; the choice is kept per browser.
- The background picture, glass card and headline texts of the sign-in pages are set by an Admin in [Settings > Sign-in page](settings.md#sign-in-page-settingssign-in-page).

## Password rules

At least 12 characters with upper case, lower case, a number and a symbol; no common passwords or look-alikes (`P@ssw0rd2026!`), no keyboard or number sequences, nothing from the user's name or email, and none of the last 5 passwords.

## New users

An Admin adds users in **Settings > User roles > Add user**: name, user name, email, roles, user group, location, and how the user gets a password:

- **Email a link to set it** (normal): the user receives an email with a link (valid 72 hours) to choose their own password. Until then the account cannot sign in. **Send password link** (envelope icon) sends a fresh link; older links stop working.
- **Set a temporary password** (when email is not available, e.g. before SMTP is set up): the Admin types a password that meets the password rules. No email is sent. The Admin gives the password to the user in person or by phone. At first sign-in the user must choose their own password before they can use the app, and they cannot keep the temporary one.

## Managing existing users

In **Edit access** (another user's account; Admins cannot do these to their own):

- **Set temporary password**: as above, for a user who cannot receive email or is locked out. Their current password and open links stop working, they are signed out, and they must choose a new password at next sign-in.
- **Deactivate**: the user is signed out within 15 minutes (when their access token expires) and cannot sign in or use password links. Roles and history are kept. **Reactivate** restores the account with its previous password.
- **Remove user**: deletes the account with its roles, password history and links. The sign-in audit rows stay (without the link to the user). Use it for mistaken or test accounts; for people who have used the system, deactivate instead.

The last active Admin cannot be deactivated or removed.

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
- Every sign-in, failure, lockout, password change, access change and role change is written to `auth.LoginAudit` with the client IP; so are temporary passwords set by an Admin (`PasswordSetByAdmin`), deactivation (`UserDeactivated` / `UserActivated`) and removal (`UserDeleted`, with the user name, email and who removed it).
- Admins never see a user's own password. A temporary password set by an Admin is hashed like any other and must be changed at first sign-in.
- Sign-in and password endpoints: at most 10 requests per minute per IP.
