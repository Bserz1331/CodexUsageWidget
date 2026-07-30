# Changelog

## 2.3.0

- Added a compact dark-themed support dialog accessible from the tray menu.
- Added local QR codes and one-click copy actions for USDT BEP20 and TRC20 addresses.
- Added a Ko-fi link and explicit network safety guidance.
- Kept application updates in notification-only mode; the app never replaces its own EXE.

## 2.2.4

- Added startup self-repair for invalid settings and off-screen window positions.
- Added automated coverage for settings recovery, quota color boundaries, and multi-monitor placement.

## 2.2.3

- Fixed a native layered-window style conflict that could leave the tray process running without a widget window.
- Strengthened existing-window activation when the EXE is launched a second time.

## 2.2.2

- Synchronized the progress bar with green, orange, and red quota states.
- Improved compact text alignment and anchored the percentage to prevent visual shifting.
- Increased progress-bar clarity and softened the window border.

## 2.2.1

- Improved text rendering at fractional Windows DPI scales.
- Replaced hard region clipping with DWM rounded corners.
- Fixed concurrent diagnostic log writes.
- Tightened weekly-window and percentage validation.

## 2.2.0

- Migrated the application to .NET 8 WinForms.
- Added xUnit parser tests and GitHub Actions CI.
- Added self-contained single-file Windows publishing.
- Added event-driven session monitoring and fallback checks.
- Prevented older quota records from replacing newer data.
- Added quota-stall detection and improved diagnostic logging.
- Moved history cleanup to a background worker.
- Added dynamic high-contrast tray percentage icons.
- Added persistent widget visibility, locking, opacity, and click-through state.
