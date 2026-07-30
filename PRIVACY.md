# Privacy

Codex Usage Widget performs all processing locally.

The application:

- reads JSONL files under `%USERPROFILE%\.codex\sessions`;
- extracts only timestamp and rate-limit fields needed for the widget;
- does not read `%USERPROFILE%\.codex\auth.json`;
- does not transmit prompts, source code, session contents, usage information,
  settings, diagnostics, or telemetry;
- stores settings, limited usage history, and error logs under
  `%LOCALAPPDATA%\CodexUsageWidget`.

Users can remove all locally stored widget data by closing the application and
deleting `%LOCALAPPDATA%\CodexUsageWidget`.

Do not attach complete Codex session files to public bug reports. Use the
built-in "Copy diagnostics" action instead.
