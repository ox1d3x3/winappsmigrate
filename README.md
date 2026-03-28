# Win Apps Migrator V0.0.6

This package contains the WPF/.NET 8 Windows prototype for Win Apps Migrator.

## Highlights in V0.0.6
- cleaner card-based desktop UI with click-anywhere app selection
- improved running-app prompt with visible continue, skip, and cancel actions
- restore flow now prefers WinGet's `winget` source directly to avoid `msstore` source issues
- restore now checks whether an app already appears installed before attempting WinGet
- fallback WinGet name search added when a backup entry has no package ID
- WinGet log output is cleaned up to reduce spinner/progress-bar garbage in the activity view

## Build
1. Open `AppMigrator.sln` in Visual Studio 2022 or later.
2. Ensure the **.NET desktop development** workload is installed.
3. Restore NuGet packages.
4. Build or publish the `AppMigrator.UI` project.

## Publish suggestion
- Configuration: `Release`
- Deployment mode: `Framework-dependent` or `Self-contained`
- Target runtime: `win-x64`
- Single file: enabled if desired

## Notes
- Best results come from restoring after the target app is installed.
- Close browsers, sync clients, editors, and Adobe apps before backup or restore.
- If WinGet sources are broken on the target machine, run `winget source reset --force` and `winget source update` once from an elevated terminal.
