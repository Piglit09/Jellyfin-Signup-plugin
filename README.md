# Jellyfin Signup Plugin

Public signup, email verification, password reset, and login-page signup button support for Jellyfin.

This plugin adds a standalone Jellyfin-branded signup page plus an admin dashboard for managing account creation. It is built for Jellyfin `10.11.10` and targets `.NET 9`.

## Features

- Public signup page at `/signup.html`
- Optional invite-code requirement
- Admin-created invite codes with labels, max uses, expiration, policy selection, folder overrides, and email-required controls
- Email verification before the Jellyfin user is created
- Automatic redirect to the Jellyfin login page after successful email verification
- Password reset by email code verification
- Signup-created user email tracking
- Admin-managed email mappings for existing Jellyfin users so password resets can work for them too
- Native Jellyfin login-page signup button injection through File Transformation
- Plugin password reset replacement for the Jellyfin login page's stock forgot password action
- Public signup page appearance customization
- SMTP email delivery with test email support
- Signup/rate-limit cleanup for pending verifications, reset codes, and failed attempts

## Compatibility

| Item | Value |
| --- | --- |
| Plugin version | `0.1.4` |
| Target Jellyfin ABI | `10.11.10.0` |
| Target framework | `net9.0` |
| Package | `artifacts/Jellyfin.Plugin.Signup_0.1.4.zip` |
| Jellyfin manifest checksum (MD5) | `0377358777DB9742AF667A123FD31717` |

## Installation

### Prerequisite For Login-Page Patching

Install **File Transformation** if you want the plugin to modify Jellyfin's login page directly. The public `/signup.html` page still works without it.
After updating login-page patching, restart Jellyfin and hard refresh the browser or clear Jellyfin site data so cached web bundles are reloaded.

Add this repository in Jellyfin, then install **File Transformation** from the catalog:

```text
https://www.iamparadox.dev/jellyfin/plugins/manifest.json
```

Install Jellyfin Signup from the plugin repository:

```text
https://raw.githubusercontent.com/Piglit09/jellyfin-plugin-signup-repo/main/manifest.json
```

1. Open the Jellyfin dashboard.
2. Go to **Plugins**.
3. Open **Repositories**.
4. Add a new repository:
   - Repository name: `Jellyfin Signup`
   - Repository URL: `https://raw.githubusercontent.com/Piglit09/jellyfin-plugin-signup-repo/main/manifest.json`
5. Go back to **Catalog**.
6. Install **Jellyfin Signup**.
7. Restart Jellyfin if prompted.
8. Open the Jellyfin dashboard and configure **Jellyfin Signup**.

Repository source: [Piglit09/jellyfin-plugin-signup-repo](https://github.com/Piglit09/jellyfin-plugin-signup-repo)

### Local Build Package

```powershell
dotnet build Jellyfin.Plugin.Signup.csproj -c Release
dotnet publish Jellyfin.Plugin.Signup.csproj -c Release -o artifacts\publish
Compress-Archive -Path artifacts\publish\* -DestinationPath artifacts\Jellyfin.Plugin.Signup_0.1.4.zip -Force
```

## Configuration

Open the plugin configuration page from the Jellyfin dashboard.

### General

- Enable or disable public signup.
- Require or disable invite codes.
- Copy the public signup URL.
- Configure the Jellyfin login-page signup button.
- Replace Jellyfin's login-page forgot password action with the plugin reset flow.
- Choose the default policy preset for public signups.
- Set default folder IDs for new users.

When **Require invite code** is disabled, the public signup page hides the invite code field and creates users using the default policy settings.

### Email & Users

- Enable email verification and password reset.
- Configure SMTP host, port, username, password, sender address, and SSL/TLS.
- Set the public email verification URL.
- Set verification-link and reset-code lifetimes.
- Send a test email.
- View and edit user email mappings used by password reset.

For verification links to work, the **Email verification URL** must be reachable by email recipients. Example:

```text
https://example.com/signup.html
```

or, for a LAN-only server:

```text
http://10.16.0.143:8096/signup.html
```

### Appearance

Customize the public signup page:

- Browser title
- Heading
- Intro text
- Logo URL
- Background color
- Panel color
- Text colors
- Accent colors

### Invites

Create invite codes with:

- Admin label
- Max uses
- Expiration date
- Policy preset
- Folder ID overrides
- Email-required setting

Invite codes are only shown once after creation, so copy them before leaving the page.

## Signup Flow

1. A user opens `/signup.html`.
2. If invite codes are required, they enter a valid invite code.
3. The user enters username, email, and password.
4. The plugin sends a verification email.
5. The user clicks the verification link.
6. The plugin creates the Jellyfin user, applies the selected policy, stores the email mapping, and redirects the user to the Jellyfin login page.

## Password Reset Flow

1. A user opens `/signup.html` and selects **Reset Password**.
2. The user enters the email mapped to their Jellyfin account.
3. The plugin sends a reset code by email.
4. The user enters the code and a new password.
5. The plugin verifies the code and changes the Jellyfin password.

For existing Jellyfin users, add their email in the **Email & Users** tab. Signup-created users are tracked automatically.

## Release Manifest

`manifest.template.json` contains the current plugin manifest entry. Before publishing a public release, replace the placeholder release URL:

```json
"sourceUrl": "https://github.com/Piglit09/jellyfin-plugin-signup-repo/releases/download/0.1.4/Jellyfin.Plugin.Signup_0.1.4.zip"
```

with the real GitHub release URL.

## Security Notes

- Invite codes are stored hashed.
- Email verification tokens and reset codes are stored hashed.
- Pending signup passwords are protected using Jellyfin data protection.
- SMTP passwords are not returned by the admin API.
- Rate limiting is applied to signup, verification, and reset endpoints.

## Development

Useful checks:

```powershell
dotnet build Jellyfin.Plugin.Signup.csproj -c Release
Get-Content -Path Pages\config.js -Raw | node --input-type=module --check
$html = Get-Content -Path Pages\public.html -Raw
$script = [regex]::Match($html, '(?s)<script>(.*?)</script>').Groups[1].Value
$script | node --check
```

## Status

This is an early `0.1.4` plugin. Test on a non-critical Jellyfin instance before using it for production account creation.
