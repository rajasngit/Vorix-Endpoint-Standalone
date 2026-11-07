VORIX ENDPOINT — WINDOWS STANDALONE 1.2.1 PILOT
Powered by ClamAV. Copyright © 2026 Vorix.

Windows 11 x64; .NET Framework 4.8 (included with Windows 11).
Allow roughly 2 GB disk space plus scan logs, and at least 4 GB free RAM.
Internet access is required for the initial definition download and daily updates.
No HO server connection, enrollment token, separate Python installation or DNS change.
A private bundled Python runtime runs the background security monitor.

INSTALL
1. Run Vorix-Standalone-Setup-1.2.1-x64.exe and approve administrator access.
2. Choose Install / repair, or Uninstall for an existing standalone installation.
   For the portable ZIP, extract it and run Install.cmd instead.
3. Open Vorix Endpoint from the Start menu. Wait for the initial definitions
   download to finish before scanning. Use Update definitions to retry a failure.

This pilot executable is not code-signed. Organisation deployment should use a
reviewed, signed build. Do not disable Windows security controls to install it.

DAILY UPDATES
Windows Task Scheduler task: VorixStandaloneDefinitions.
Runs as SYSTEM daily at 12:00 local time, catches up after missed runs, and starts
the first download immediately after installation. Network access is needed.
Uses official FreshClam and DatabaseMirror database.clamav.net; downloaded
databases are tested before use. FreshClam handles rate limits and incremental
updates. Definition updates do not upgrade the ClamAV executable.
Check update status and errors in the application and:
C:\ProgramData\Vorix Endpoint\logs\freshclam.log

SCANNING
Choose a file, folder, or all ready local fixed and removable drives. Stop scan cancels the scan;
cancelled or failed scans must never be treated as clean. Scan logs are saved to
%LOCALAPPDATA%\Vorix Endpoint\logs and can include filenames and findings.
Manual and scheduled scan detections move to protected quarantine for review.
Automatic download checks scan a protected snapshot and report findings without
changing or blocking the original file. Defender provides real-time blocking.
Normal user permissions may prevent scanning protected files. For broader
coverage, run the Start menu application as administrator. Symlinks are not
followed. ClamAV size/archive/time limits still apply; review the complete log.

LOCAL SECURITY ALERTS AND DOWNLOAD MONITORING
The background VorixStandaloneMonitor task runs as SYSTEM at startup, even while
signed out. It reports Defender detection history locally and checks new or
changed completed files in local user Downloads folders. The first inventory
baselines existing files; use Full scan for those. Incomplete downloads wait.
Checks are bounded to 500 MiB per file, three files per pass, 5000 files per
inventory and eight subdirectory levels. Linked/network folders are excluded.
Monitoring errors, skipped files and limits appear in the status icon.

The clear Vorix status icon starts at sign-in. Start menu > Vorix Endpoint status
can launch it immediately. Green means monitoring is healthy; amber indicates
unknown/limited monitoring; red indicates a stopped monitor or unresolved alerts.
The icon has no Close command and starts automatically at sign-in. Windows may
put it under the hidden-icons arrow; drag it beside the clock to keep it visible.
Choose Security alerts
from the app or icon to review local results. Administrator access is required
for detailed affected paths. Private history stays under:
C:\ProgramData\Vorix Endpoint\private\monitor\history.json
No detections or file contents are sent to a management console. This is on-device
file checking; it does not scan an email service before mail delivery.

REAL-TIME PROTECTION
ClamAV supplies on-demand Windows scanning. Keep Microsoft Defender or an
approved Windows real-time antivirus enabled. This package does not enable,
disable or replace Defender, and does not register as a Windows Security provider.

EXISTING MANAGED PILOT
Uses separate folders and task names; does not alter EndpointConsole agent tasks,
USB restrictions, browser policies, DNS, HO connection, or any running scan.
USB policy management remains in the managed pilot, not this standalone GUI.

REMOVAL
The setup EXE has its own Uninstall button and can remove older standalone builds
even when they did not register an uninstaller. Close the standalone app first.
Alternatively use Settings > Apps > Installed apps > Vorix Endpoint Standalone > Uninstall,
or the Uninstall button inside the app. Removal requires administrator approval.
Removal deletes standalone program files, downloaded definitions, settings,
scheduled updates, registration, shortcuts and per-user scan logs. Temporary
uninstaller/maintenance files remove themselves after the process exits. There is
no keep-data option. Standalone creates no USB policies; independent installed
products remain outside its scope. Managed cleanup restores its own USB settings.

The app shows Administrator: Yes/No, includes Check administrator, and offers
Run as administrator. Scanning can run without administrator rights but protected
files may be skipped. Installation, update scheduling and removal require admin.
Definition updates distinguish success, already-running and failure messages.
Open update log displays actual FreshClam output. Automatic and manual updates
use one exclusive lock to prevent overlapping downloads.

LICENSES / CREDITS
Vorix application source is supplied in source under GPL-2.0-only.
To rebuild it, run source\Build-Application.ps1 with Windows PowerShell and the
.NET Framework C# compiler. The upstream archive contains ClamAV build files.
ClamAV 1.5.4 is an unmodified Cisco Talos / ClamAV component. Original GPL and
third-party notices are in clamav\COPYING.txt and clamav\COPYING.
ClamAV is a trademark of Cisco Systems, Inc.; no endorsement is implied.
Corresponding upstream source is supplied in source\clamav-1.5.4.tar.gz.
Official project: https://www.clamav.net/
Official documentation: https://docs.clamav.net/
Vorix logo remains the property of its owner.




WEEKLY SILENT FULL SCAN
Enabled by default: Sunday at 02:00 local time, running as SYSTEM.
Change the weekday/time or disable it in Settings / schedules. Missed runs catch
up when the computer is available. No scan automatically deletes files.
Task: VorixStandaloneFullScan. Summaries appear in the app; detailed full-system
logs are administrator-only under ProgramData/Vorix Endpoint/private/scan-logs.

APP UPDATES
Check app updates checks public GitHub releases in
https://github.com/rajasngit/Vorix-Endpoint-Standalone
Hourly background checks are enabled by default; prompt-before-upgrade is the
default mode. Select automatic upgrades or disable checks in Settings / schedules.
Automatic upgrades defer while the app/scanner is open. Definition updates remain
daily at noon. Only signed release metadata with a matching installer SHA-256 is
accepted. This publisher signature is separate from Windows Authenticode signing.
App updates preserve settings and definitions. Uninstall removes all three tasks.

QUARANTINE REVIEW
Detections are automatically moved into an administrator-only, non-executable
vault. Choose Review quarantine, then Restore (reviewed as safe) or Delete.
Restore never overwrites an existing file and does not create an exclusion.
Failures to move files remain unresolved detections in scan output.
Microsoft Defender manages its own real-time quarantine in Windows Security.
