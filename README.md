# AppMigrator MVP

A Windows desktop prototype for backing up and restoring selected **supported application state** on a fresh Windows installation.

## What this MVP does

- Scans installed applications from Windows uninstall registry keys
- Classifies known apps using built-in rules
- Lets you select one or multiple apps
- Backs up known data folders and selected registry keys into a single ZIP
- Restores backed-up data and registry from a ZIP
- Optionally tries a **WinGet reinstall** during restore for known apps that have a package ID

## Important reality check

This is **not** a universal app cloner.

This MVP is designed around a **hybrid migration model**:
- portable apps can often be copied back
- profile-based apps should usually be **installed first**, then restored
- complex apps with services, drivers, machine licensing, or Store packaging are not promised to restore cleanly

## Supported apps in this MVP

- Google Chrome
- Microsoft Edge
- Mozilla Firefox
- Visual Studio Code
- Obsidian
- Notepad++
- FileZilla
- PuTTY
- Windows Terminal
- Git

## Requirements

- Windows 10/11
- .NET 8 SDK or Visual Studio 2022+
- Run as Administrator for best results
- WinGet recommended for reinstall-assisted restore

## Build

### Visual Studio
1. Open `AppMigrator.sln`
2. Build the solution
3. Run `AppMigrator.UI`

### CLI
```powershell
dotnet build .\AppMigrator.sln
dotnet run --project .\src\AppMigrator.UI\AppMigrator.UI.csproj
```

## Test flow

### Backup
1. Run the app as Administrator
2. Click **Scan Installed Apps**
3. Select one or more supported apps
4. Click **Backup Selected**
5. Save the ZIP somewhere safe

### Restore
1. Fresh Windows install
2. Reinstall the target apps first where practical
3. Run AppMigrator as Administrator
4. Click **Restore Backup ZIP**
5. Choose whether to attempt WinGet reinstall
6. Review the log after restore

## Notes

- Browsers should usually be closed during backup/restore.
- Some files may be skipped if they are locked.
- Cache folders are excluded for some apps to keep backup sizes sane.
- Registry export/import uses `reg.exe`.
- This prototype does not yet use VSS.

## Project layout

```text
AppMigrator/
  src/
    AppMigrator.UI/
      Models/
      Services/
      Helpers/
```

## Next upgrades I would recommend

- VSS-backed capture for locked files
- Better validation after restore
- Rule packs stored as JSON
- Differential backups
- Service/task recreation for selected supported apps
- Stronger version compatibility checks
