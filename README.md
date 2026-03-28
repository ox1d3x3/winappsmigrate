# Win Apps Migrator V0.0.3

This package contains the WPF/.NET 8 Windows prototype for Win Apps Migrator.

## Highlights in V0.0.3
- redesigned desktop UI with a cleaner two-panel layout
- larger app cards with clearer support, confidence, and restore strategy presentation
- modernized running-app prompt styling
- responsive progress and activity area preserved
- versioning updated to V0.0.3

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


V0.0.4: fixed single-file startup by removing runtime file-based window icon dependency and added startup crash logging to %LOCALAPPDATA%\WinAppsMigrator\startup-crash.log.
