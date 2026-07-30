# Security Policy

## Reporting a vulnerability

Please report security issues privately to the repository maintainer rather
than opening a public issue. Include the application version, Windows version,
and minimal reproduction steps. Do not include Codex session files, prompts,
source code, credentials, or authentication data.

## Scope

The application reads local Codex session JSONL files and writes only to its
own directory under `%LOCALAPPDATA%\CodexUsageWidget`, except when the user
explicitly enables Windows startup.
