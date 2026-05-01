# Configuring Gmail for PlainSight Device Alerts

PlainSight sends offline/recovery alert emails via SMTP. This guide covers setting up a Gmail account as the sender using an App Password, which works with standard username/password SMTP (no OAuth required).

## Prerequisites

- A Google account dedicated to PlainSight alerts (e.g. `plainsight-alerts@gmail.com`). Using a dedicated account keeps alert emails separate from personal mail and avoids exposing a personal account's App Password.
- 2-Step Verification must be enabled on that Google account — App Passwords are not available without it.

## Step 1 — Enable 2-Step Verification

1. Sign in to the Google account at [myaccount.google.com](https://myaccount.google.com).
2. Go to **Security** → **How you sign in to Google**.
3. Click **2-Step Verification** and follow the prompts to enable it.

## Step 2 — Create an App Password

1. Return to **Security** in myaccount.google.com.
2. Under "How you sign in to Google", click **2-Step Verification**, then scroll to the bottom and click **App passwords**.  
   *(If you don't see "App passwords", make sure 2-Step Verification is enabled and you are not using a Google Workspace account with that feature disabled by an admin.)*
3. In the **App name** box, enter `PlainSight` (or any label you will recognise).
4. Click **Create**.
5. Google shows a 16-character password such as `abcd efgh ijkl mnop`. **Copy it now** — it will not be shown again.
6. Remove the spaces before pasting (i.e. use `abcdefghijklmnop`).

## Step 3 — Configure PlainSight

### Option A — appsettings.json (development / on-premises)

Edit `src/PlainSight.Server/appsettings.json` (or `appsettings.Production.json`):

```json
"Alerts": {
  "Enabled": true,
  "OfflineThresholdMinutes": 5,
  "Email": {
    "To": "admin@church.org",
    "From": "plainsight-alerts@gmail.com",
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": 587,
    "Username": "plainsight-alerts@gmail.com",
    "Password": "abcdefghijklmnop"
  }
}
```

> **Do not commit real credentials** into source control. Use `appsettings.Production.json` (excluded from git) or environment variables instead.

### Option B — Environment variables (Docker / Docker Compose)

All `Alerts` settings can be overridden with environment variables using the standard ASP.NET Core double-underscore convention:

```env
Alerts__Enabled=true
Alerts__OfflineThresholdMinutes=5
Alerts__Email__To=admin@church.org
Alerts__Email__From=plainsight-alerts@gmail.com
Alerts__Email__SmtpHost=smtp.gmail.com
Alerts__Email__SmtpPort=587
Alerts__Email__Username=plainsight-alerts@gmail.com
Alerts__Email__Password=abcdefghijklmnop
```

In `docker-compose.yml`:

```yaml
services:
  plainsight-server:
    environment:
      - Alerts__Enabled=true
      - Alerts__Email__To=admin@church.org
      - Alerts__Email__Username=plainsight-alerts@gmail.com
      - Alerts__Email__Password=abcdefghijklmnop
```

For secrets in production use Docker secrets or a `.env` file that is **not** committed to git.

## Step 4 — Verify

Restart the server. Watch the logs for:

```
DeviceMonitorService started; checking every 60s, offline threshold = 5 min
```

To test without waiting for a real outage, temporarily set `OfflineThresholdMinutes` to `1` and power off a device. You should receive an email within 2 minutes.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| "Authentication failed" | Wrong App Password or spaces not removed |
| "Less secure app" error | You are using the account password instead of an App Password |
| No email, no error logged | `Alerts:Email:To` / `Username` / `SmtpHost` is blank — check config |
| Emails stopped after many alerts | Google may have rate-limited the account; check Gmail Sent folder |
| `SYSLIB0021` compiler warning | Expected — `SmtpClient` is deprecated in .NET; functionality is unaffected. Migrate to MailKit to eliminate it. |

## Disabling alerts

Set `Alerts:Enabled` to `false` (or the env var `Alerts__Enabled=false`). The `DeviceMonitorService` exits immediately on startup and no database queries are made.
