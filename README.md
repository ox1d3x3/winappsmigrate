# Win Apps Migrator

**Win Apps Migrator** is a Windows application backup and restore utility designed to help users migrate supported app data, profile data, selected settings, and related user-state content between Windows installations.

The project focuses on practical migration workflows for supported applications such as browsers and other profile-based apps, while also providing backup and restore logging, verification steps, and reinstall assistance through WinGet where applicable.

## Important Disclaimer

> **This app is still in beta phase. Do not solely rely on this app. Please back up your data separately before using it.**

Win Apps Migrator is still under active testing and refinement. While care has been taken to improve backup, restore, verification, and reinstall logic, this software should be treated as a **beta utility**.

Always keep at least one separate manual backup of your important files, application data, browser profiles, exported settings, license information, and any irreplaceable content.

## Current Beta Status

The application is currently focused on:

- Detecting installed Windows applications
- Identifying supported applications through built-in migration rules
- Backing up supported application data into a ZIP archive
- Restoring supported application data to the correct user profile paths
- Attempting reinstall of missing supported apps through WinGet before restore
- Generating logs and verification output for backup and restore operations

## Current Testing Notes

In current beta testing, **supported browser restores have been tested with data intact in successful test cases**.

This includes profile-based restore scenarios where browser-related user data was backed up and restored successfully during testing. Broader app compatibility is still being expanded and refined.

Because this is beta software, results can vary depending on:

- Application version differences
- Windows version differences
- Whether the target app is closed during backup/restore
- Licensing, machine-bound settings, services, or driver dependencies
- WinGet source availability and package match accuracy


## Screenshots
<img width="1395" height="904" alt="image" src="https://github.com/user-attachments/assets/24daefb5-a1cd-48be-a18d-efb020b26838" />
<img width="1399" height="904" alt="image" src="https://github.com/user-attachments/assets/cf7c9733-c227-4991-87b7-69d4c9002bfe" />



## What the App Is Intended to Do

Win Apps Migrator is intended to make application migration easier by handling the parts that usually take the most time after reinstalling Windows:

- App profile folders
- User configuration data
- Selected registry-backed settings where supported
- Rule-based restore targeting
- Reinstall assistance for missing apps
- Backup and restore reporting

For supported applications, the goal is to restore the user environment as cleanly as possible without requiring the user to manually hunt through AppData, registry paths, or scattered configuration folders.

## What the App Is Not Meant to Guarantee

This tool does **not** guarantee a perfect full-clone migration of every Windows application.

Some software types are more complex and may require extra care, manual reinstall, sign-in, activation, or manual validation after restore, especially:

- Adobe and other large creative suites
- Security software and antivirus products
- VPN clients and software with network drivers
- Hardware control software
- Licensed or machine-bound applications
- Store-packaged apps with special dependencies
- Apps with services, drivers, or protected components

## Recommended Usage

For best results:

1. Close selected apps before starting backup or restore.
2. Keep a separate manual backup of important data.
3. Test restore with a small set of supported apps first.
4. Verify restored data before deleting your previous backup.
5. Use the generated logs and verification report after each run.

## Verification and Safety

The application includes backup and restore logging and verification features to help users review what happened during the process.

Users should still manually confirm that:

- The restored app launches correctly
- Important data is present
- Profiles and settings are intact
- No critical files are missing
- Sign-in and sync state behave as expected

## Project State

Win Apps Migrator is under active beta development with ongoing work across:

- UI refinement
- App rule coverage
- Restore reliability
- WinGet reinstall handling
- Verification improvements
- Theme and workflow polish

## Author

**Author:** Ox1d3x3

## Releases

GitHub Releases:

<https://github.com/ox1d3x3/winappsmigrate/releases>

## Final Note

Win Apps Migrator is built to reduce the pain of rebuilding your Windows app environment after a fresh installation, but beta software should always be used with caution.

> **Please do not depend on this app as your only backup method. Keep a separate backup of your important data before using it.**
