# Vorix Endpoint Standalone

Windows endpoint antivirus scanning, powered by **ClamAV**, with the Vorix interface. No separate Python installation, central server, enrollment token, or DNS change is required. The background monitor uses a private bundled Python runtime.

[Download the latest Windows x64 installer](https://github.com/rajasngit/Vorix-Endpoint-Standalone/releases/latest)

## Features

- File, folder and full ready local fixed/removable-drive scans. Detections move to a protected, non-executable quarantine vault. Review and restore safe files or explicitly delete them; move failures remain unresolved.
- Silent weekly full-system scan, enabled by default on Sunday at 02:00 local time. Enable/disable and choose the day/time in **Settings / schedules**.
- Official ClamAV definitions refreshed daily at 12:00 using FreshClam, with missed-run catch-up.
- Green/red administrator status and elevation controls.
- Manual app-update checks and optional hourly background checks. The default prompts while the app is open; automatic mode upgrades when the app/scanner is closed.
- Pinned RSA/SHA-256 release metadata verification and installer hash checks. No GitHub token is stored on a client.
- Install/repair and complete uninstall from Setup, the app, or Windows Installed Apps. Uninstall removes the scheduled tasks and status-icon startup entry, definitions, settings, program files and product logs. Quarantined files must first be reviewed and restored or explicitly deleted.

Keep Microsoft Defender enabled for real-time Windows protection. ClamAV provides on-demand and scheduled scans; this app does not provide ClamAV on-access scanning on Windows or standalone USB control.

## New in 1.2.1

- The status icon no longer offers a Close command. It starts at sign-in, and interactive installations launch it immediately.
- Corrected monitor detection for ordinary users: the tray checks process names and IDs without opening a privileged handle to the SYSTEM worker.
- Windows controls icon overflow visibility; drag Vorix from the hidden-icons area beside the clock to keep it in view.

## New in 1.2.0

- Automatic ClamAV checks of new or changed completed files in local Downloads folders. Findings are reported for review without changing the original; Microsoft Defender provides real-time blocking.
- Local Defender detection history with action results and remaining remediation requirements. Detailed affected paths require administrator access.
- A clear Vorix notification-area icon with monitor health and unresolved-alert indication. Close the icon without stopping the background monitor.
- Security alerts available from the app and icon. No alert data is sent to a central console.
- Full scans discover all ready local fixed and removable drives each time.

Existing downloads are baselined on the first inventory; use Full scan for them. Extra download checks have file-size, inventory and depth limits, shown in monitoring status. This does not scan a mail service before delivery. The monitor runs as SYSTEM at startup; the icon starts at sign-in or from **Vorix Endpoint status** in Start.

## Requirements and installation

Windows 11 x64 with .NET Framework 4.8, administrator rights for installation/settings changes, internet access, approximately 2 GB disk space plus logs, and 4 GB available RAM. Run the installer, choose **Install / repair**, then open Vorix Endpoint from Start. Wait for the first definitions download before scanning. Restart is not normally required.

Detailed scan logs for SYSTEM scans are administrator-only in `C:\ProgramData\Vorix Endpoint\private\scan-logs`; the app shows a summary. Schedules run even when signed out and catch up when the computer is available. Repairs/upgrades retain definitions and settings. Remove an existing HO-managed client separately before switching to standalone.

## Build from source

Run `powershell -ExecutionPolicy Bypass -File .\build.ps1` on Windows x64. The script fetches the pinned official ClamAV 1.5.4 archive and matching source tarball plus the official embedded Python runtime, checks SHA-256, and builds the native .NET app and installer. The redistributable installer includes source and third-party licenses. Output is under `dist/`.

No private signing key, deployment configuration, endpoint identity, or user logs belong in this repository. Release metadata is signed outside GitHub using the publisher's protected private key. Forks must substitute their own public update key and repository path before distributing their own updates.

## Release trust and limitations

The pinned release signature is distinct from Windows Authenticode: pilot installers are currently **not Authenticode signed**, so Windows may display an unknown-publisher prompt. Never disable Windows security protections to install. The app refuses unsigned/tampered release metadata and mismatched installer bytes, and never downgrades automatically. It leaves running scans/app sessions undisturbed and retries automatic upgrades later.

## License and credits

GPL-2.0-or-later; see [LICENSE](LICENSE) and the vendor notices bundled with the installer. ClamAV is copyright Cisco Systems, Inc. and contributors. Vorix interface copyright © 2026 Vorix. ClamAV and Microsoft Defender are independent projects/products; no vendor affiliation is implied.

Use **Review quarantine** as an administrator to restore or delete an isolated file. Restores never overwrite existing paths and never add an automatic exclusion. Microsoft Defender maintains its own real-time quarantine in Windows Security.
