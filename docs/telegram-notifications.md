# Telegram failure notifications

IKAutomation sends a short Telegram message for technical One-Shot Farm failures.
Expected business outcomes (resource not found, exhausted levels, no eligible team,
storage full, ready-team wait timeout, and user cancellation) are not sent.

## Secure setup on Windows CMD

1. Revoke any token that has been pasted into chat, source code, screenshots, or logs.
   In BotFather, open the bot and use `/revoke`, then generate a new token.
2. Open the bot in Telegram and send `/start` from the account that should receive alerts.
3. In a new CMD window, temporarily set the new token for that process:

   ```cmd
   set "IKAUTOMATION_TELEGRAM_BOT_TOKEN=PASTE_NEW_TOKEN_HERE"
   ```

4. Read the latest update and find `message.chat.id`:

   ```cmd
   curl "https://api.telegram.org/bot%IKAUTOMATION_TELEGRAM_BOT_TOKEN%/getUpdates"
   ```

5. Persist the new token and chat ID for the Windows user:

   ```cmd
   setx IKAUTOMATION_TELEGRAM_BOT_TOKEN "PASTE_NEW_TOKEN_HERE"
   setx IKAUTOMATION_TELEGRAM_CHAT_ID "PASTE_CHAT_ID_HERE"
   ```

6. Close and reopen IKAutomation. `setx` does not update applications that are
   already running.

If IKAutomation is launched from Visual Studio, close and reopen Visual Studio
after `setx`. Visual Studio otherwise starts the application with its old
environment. On startup, the diagnostic window reports whether Telegram is
configured.

Verify the variables in a newly opened CMD without printing their values:

```cmd
if defined IKAUTOMATION_TELEGRAM_BOT_TOKEN (echo BOT_TOKEN=CONFIGURED) else (echo BOT_TOKEN=MISSING)
if defined IKAUTOMATION_TELEGRAM_CHAT_ID (echo CHAT_ID=CONFIGURED) else (echo CHAT_ID=MISSING)
```

The token and chat ID are read only from environment variables. They are never
stored in `App.config`, user preferences, diagnostic screenshots, or Git.

Telegram Bot API messages are limited to 4096 characters. IKAutomation sends a
bounded summary containing device, outcome, last completed step, resource, level,
team, message, error, and diagnostic path. Delivery errors are logged without the
request URI or bot token and never replace the gameplay result.
