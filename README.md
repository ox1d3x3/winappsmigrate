# Win Apps Migrator V0.0.1

A Windows desktop prototype for backing up and restoring selected application state on a fresh Windows installation.

## What changed in V0.0.1

- Renamed the app to **Win Apps Migrator**
- Reset versioning to the requested scheme starting at **V0.0.1**
- Added support for **Brave Browser** profile migration
- Added broader profile/state support for **Vivaldi**, **Opera**, **7-Zip**, **Greenshot**, **Everything**, **Discord**, and **Adobe Acrobat** settings
- Added a **rule-pack loader** so future app support can be extended through external `*.rule.json` files without recompiling the app
- Improved detection by matching on display name, publisher, and install location instead of display name only

## What this build does

- Scans installed applications from Windows uninstall registry keys
- Classifies known apps using built-in and external rule packs
- Lets you select one or multiple apps
- Backs up known data folders and selected registry keys into a single ZIP
- Restores backed-up data and registry from a ZIP
- Optionally tries a **WinGet reinstall** during restore for known apps that have a package ID

## Important reality check

This is still **not** a universal app cloner.

This build is designed around a **hybrid migration model**:
- portable apps can often be copied back
- profile-based apps should usually be **installed first**, then restored
- settings-only apps can often restore user preferences but not shell integration, services, drivers, or licensing state
- complex apps with services, drivers, machine licensing, or Store packaging are not promised to restore cleanly

## Currently supported in this build

- Google Chrome
- Microsoft Edge
- Brave Browser
- Mozilla Firefox
- Vivaldi
- Opera / Opera GX
- Visual Studio Code
- Obsidian
- Notepad++
- FileZilla
- PuTTY
- Windows Terminal
- Git
- 7-Zip
- Greenshot
- Everything
- Discord
- Adobe Acrobat settings/profile data

## Extending support with rule packs

The app can load additional rule packs from a `rules` folder beside the executable.

Each file should be named like:

```text
something.rule.json
```

Each file should contain a JSON array of `AppRule` objects, matching the properties in `Models/AppRule.cs`.

This is the architecture pivot that makes the project scalable instead of hard-coding every app forever.

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

## Publish

Recommended Visual Studio publish settings:
- Configuration: `Release`
- Deployment mode: `Self-contained`
- Target runtime: `win-x64`
- Produce single file: `On`

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
3. Run Win Apps Migrator as Administrator
4. Click **Restore Backup ZIP**
5. Choose whether to attempt WinGet reinstall
6. Review the log after restore

## Notes

- Browsers should usually be closed during backup/restore.
- Some files may be skipped if they are locked.
- Cache folders are excluded for many Chromium/Electron-style apps to keep backup sizes sane.
- Registry export/import uses `reg.exe`.
- This prototype does not yet use VSS.
- Adobe Creative Cloud suites still need deeper app-specific rules. Acrobat support here is **settings/profile focused**, not a full Adobe suite migration claim.

## Project layout

```text
WinAppsMigrator/
  src/
    AppMigrator.UI/
      Models/
      Services/
      Helpers/
      rules/
```
