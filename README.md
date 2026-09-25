# Equipment Tracking Platform — 0.9.6-alpha.1

A Windows desktop application for creating, signing, searching, updating, recovering, printing, closing, and archiving DD Form 1297 equipment records.

The application targets C# / .NET 10 / WPF, stores operational data in local SQLite, and has no runtime Internet dependency, telemetry, or automatic network updater. This remains a pilot build; it is not an AFNET approval, Authority to Operate, or records-policy determination.

## Field deployment artifacts

After an intake is finalized, **Print two 1297 copies** is available directly in Intake beside the completed ticket. It uses the same readable Letter sheet as Dashboard printing. Reprint as needed without finalizing again; starting or resetting an intake clears this shortcut. Keep the printing notice open until printing finishes. Saved signed PDFs are never modified by printing.

`scripts\build-release.ps1` creates two offline, self-contained Windows x64 choices:

- `EquipmentTrackingPlatform-<version>-win-x64.msi` — the preferred managed-workstation package. It installs the conventional multi-file payload under `Program Files\58 SOW\Equipment Tracking Platform`, creates shortcuts, and supports Windows Installer major upgrades. Installation or upgrade requires an authorized administrator or software-distribution system.
- `EquipmentTrackingPlatform-<version>-win-x64.exe` — one portable, self-contained executable for a standard-user field test when installation is unavailable. Put it in an approved user-writable application folder and replace it only while the application is closed.

The two packages use the same per-user data locations. Updating or uninstalling the application does not remove the SQLite database, settings, completed PDFs, or backups. Settings now provides **Install update package…** for a newer offline MSI. It checks the product and stable UpgradeCode, refuses same/older versions, verifies the MSI hash and size against a companion `release-manifest.json` when available, creates a verified pre-update database/settings backup, starts Windows Installer with administrator approval, and closes the app. It never checks the Internet. An older installed build without this command uses the normal authorized MSI path once to reach 0.9. Every MSI release must increment one of the first three numeric version fields; for example, move from `0.9.3-alpha.1` to `0.9.4-alpha.1`, not only to `0.9.3-alpha.2`.

The current verified release is `0.9.6-alpha.1`, MSI `0.9.6`, SQLite schema 6. It adds direct two-copy printing after successful Intake finalization, preserving the readable print layout, partial-pickup cross-outs, and signed PDFs. The canonical release gate passed all 154 tests (0 failed/skipped), validation, dependency audits, offline review, both publish modes, and MSI creation. Assets are under `artifacts/release`; see `VALIDATION.md`. Publication is authorized as `v0.9.6-alpha.1`. The build has not been installed; physical printer, scanner, and Adobe/CAC acceptance remains outstanding.

The prior verified release was `0.9.3-alpha.1`. Its canonical gate passed in 54.9 seconds with 112 tests, 0 failed, and 0 skipped, plus validation, dependency audits, fail-closed offline review, both publish modes, and MSI creation. Its historical hashes and sizes remain recorded in `VALIDATION.md`. All unsigned-pilot artifacts remain restricted to synthetic-data testing unless the responsible organization explicitly approves a different use.

See `DEPLOYMENT.md` and `FIELD-TEST-CHECKLIST.md` before moving the pilot to another computer.

## Backup architecture

Scheduled backups are enabled by default and use local workstation time:

| When | Backup | Contents |
|---|---|---|
| Monday 09:00 | Full | Consistent SQLite snapshot, settings, and every PDF below the configured Completed PDF folder, including preserved originals, signed pickup receipts, and archived 1297s |
| Monday–Friday 16:00 | Differential | A complete consistent SQLite snapshot, PDFs that are new or changed since the current Monday full, and deletion markers for PDFs moved/deleted since that full |

The scheduler runs inside the application. The application must be open at 09:00 or 16:00 for an on-time run. If the application or computer is off, it creates the latest missed backup when the application next opens. It retries a failed run every 15 minutes while open.

Every scheduled archive is written to a temporary file, closed, reopened, and verified before it is accepted. Verification includes ZIP structure, SHA-256 for the database/settings/PDF payload, and SQLite `PRAGMA quick_check`. Before creation, every official PDF path referenced by the database must exist below the configured Completed PDF folder and must not cross a child reparse point. A `.sha256` sidecar is written beside each accepted archive.

After the following Monday full is successfully created, retention independently re-verifies it before prior-week differential archives and their sidecars are deleted. If the new full is missing, unreadable, or corrupt, the previous full and differentials are retained. The retention setting controls how many verified weekly full archives remain; the default is five.

A differential restore requires its named Monday full archive to remain in the same folder. Restore merges the full PDF payload with the differential overlay and deletion markers, uses the differential database snapshot, stages and re-verifies everything, creates a safety copy of current local data, and applies on the next application start. PDF restore is intentionally non-destructive: unrelated current PDF files absent from the selected backup are not automatically deleted.

Scheduled archives do not include temporary print sheets, logs, the derived Excel workbook, or unfinished Adobe working/recovery files. Excel can be regenerated; interrupted workflows should be resumed or rolled back promptly rather than treated as protected records.

The default backup folder is in Documents and normally resides on the same physical drive as the database. That protects against application/database corruption but not drive loss. For field use, configure an approved second physical volume or approved protected destination when available. The readiness check warns about a same-drive destination, FAT32, low space, overlap with protected folders, and failed write access.

Changing the backup destination or Completed PDF root invalidates the current differential base and causes the scheduler to create a new full chain. Old chains in a previous destination are not deleted across folders; retain or dispose of them only under the approved records/backup policy.

## Core workflow and safeguards

- The rank/name entered in New Intake is the authoritative customer identity for the `ISSUED TO` name row, saved transaction, Dashboard, exports, and customer-derived filename. The `ISSUED TO` PDF signer is retained separately as signature/certificate evidence and never replaces those entered fields.
- Every created 1297 uses its unique `TX-YYYYMMDD-HHMMSS-XXXXXXXX` ID. The ID and an `ETP1297:` QR payload are printed on the filled form. While Dashboard is open, typing or scanning from non-text, non-choice Dashboard/window focus starts a fresh record search, moves input to the search box, and resolves a complete active or archived record code without requiring a preliminary click or Enter. Input already focused in the search box continues to edit there natively; other text-entry and ComboBox controls also retain their native input behavior.
- Device scan rows detect scanner-speed input and submit after a short idle interval. Enter remains available for deliberate manual entry.
- ISO/IEC 15434 parsing accepts common control-token substitutions, missing header characters, manufacturer prefixes, 17V/18S, serial-before-part layouts, and the field scanner samples covered by tests. The exact raw scan remains in SQLite. The exact `18S` identity with CAGE `7ESQ7` and serial `2MQ5390WTS` resolves offline to part `A4TH1AV` and model `HP EliteBook 645`; neither the CAGE nor serial alone is treated as a match.
- Part numbers and common model names are stored separately. Operators can type multiword common model names with spaces; surrounding whitespace is normalized when the row is added or updated. Exact operator catalog mappings take precedence over the built-in name. Prior exact-serial history is reused only when its known part is unambiguous, its nonblank model names do not conflict, and any available CAGE evidence does not conflict. A missing friendly model can still come from the operator catalog or exact built-in part map; otherwise the normal editable field/prompt remains. Unknown part numbers are not guessed and remain searchable by either value.
- From an active Dashboard record, a partial pickup can select one or more exact devices by scan or checkbox, or select all devices already marked Ready for pickup. The app prepares a separate copy from the preserved original signed intake, crosses out only the selected device lines, fills `RETURN DATE`, and requires the customer's exact `Pickup Signature` before marking any selected device Returned.
- Each verified subset pickup is retained as a child document of the original parent 1297 with its device links and signer metadata. The parent stays active while devices remain; the final verified pickup archives that one parent with all of its pickup documents. The Dashboard Documents action exposes the original, current/final, and signed pickup copies. Existing full closeout and ordinary device-status editing remain available.
- Settings can install a newer approved local MSI after identity/version/manifest checks and a verified pre-update backup. There is no network updater.
- Active-CAC discovery is bound to readers that currently contain a card; cached personal-store certificates are not listed.
- Intake, closeout, and partial pickup use durable recovery journals and can resume after interruption. An incomplete pickup can be abandoned safely; a receipt already committed to SQLite cannot be rolled back, and a changed attempt is preserved as evidence.
- Official PDF copies use temporary files, flush/close, SHA-256 verification, and atomic placement.
- Original signed intake, working/status, each signed pickup receipt, final signed closeout, and archived versions remain preserved as applicable.
- Dashboard and Equipment status opening show a temporary readable current-status view with every completed pickup crossed out. Dashboard printing uses those same cumulative returned-device marks on both copies of the Letter sheet. This also works for pickups saved by 0.9.4, without modifying the database or signed PDFs.
- In Documents, select a signed pickup and use **View selected** or **Print readable copies** for that receipt's subset only. The current-status row instead shows all completed pickups, including in the archive. **Open preserved PDF** opens unchanged signature evidence. Readable reference/print copies do not replace or validate the original digital signatures.
- TextBox padding is applied once so the caret and typed text use the declared content inset. Dashboard pie slices gain a visible hover outline and a category/count/percentage tooltip; the chart exposes its automation name/help text through a peer, uses weak collection notifications so discarded visuals are not retained after navigation/refresh, and keeps the persistent labeled legend as the accessible non-hover equivalent.
- One application instance runs per signed-in Windows user.
- Startup readiness checks cover storage, SQLite, the approved template, PDF application, smart-card readers, backup destination, and free space.
- Sanitized support packages exclude the operational database and PDFs by default.

## Clean 1297 template

The included template is `src\EquipmentTracking.App\Templates\1297-58SOW-SC-TEMPLATE.pdf`.

- 24 editable form fields and 3 signature fields.
- Exact field name `Pickup Signature`.
- No PDF JavaScript; `/NeedAppearances` is false.
- SHA-256: `00daa4ac652d592f03554e1d8e2ea9ec6083b30e9c9659330fe1dad32e599051`.

Because JavaScript was removed, the application fills `DATE OF ISSUE`, `RETURN DATE`, and `QNTY`.

## Build, validate, and package

On an approved Windows development workstation with .NET SDK 10.0.110 (the pinned 1xx servicing band containing .NET runtime 10.0.10):

```powershell
dotnet clean
dotnet restore
dotnet test
.\scripts\validate.ps1
.\scripts\security-scan.ps1
.\scripts\verify-offline.ps1 -FailOnFinding
```

Create the signed portable EXE, machine-wide MSI, checksums, and release manifest:

```powershell
.\scripts\build-release.ps1 `
  -CertificateThumbprint '<approved-thumbprint>' `
  -TimestampUrl '<approved-timestamp-url>'
```

The script signs the application-owned payload before the MSI is built, signs the completed MSI, and verifies the signatures. An explicitly approved synthetic-data lab can use `-AllowUnsignedPilotBuild`; its EXE and MSI file names are marked `UNSIGNED-PILOT` and must not be used with operational records. Never commit a PFX, password, private key, or signing token.

Run only one release build at a time and close active Visual Studio/debug build sessions first. The release script stops persistent .NET build servers before deleting WPF `bin`/`obj` state and disables server reuse for every release compilation, so consecutive runs regenerate `App.g.cs`, view `.g.cs`, BAML, and related markup caches instead of referencing files removed by the prior cleanup.

The installer project pins WiX Toolset SDK 6.0.2. Confirm the owning organization accepts the applicable Open Source Maintenance Fee terms before building/distributing the MSI; do not upgrade to a new WiX major version without dependency, support, and license review.

## Data locations

Local application state:

```text
%LOCALAPPDATA%\58SOW\EquipmentTrackingPlatform
```

Default operator output and backup root:

```text
%USERPROFILE%\Documents\Equipment Tracking
```

The SQLite database must remain local to one workstation and user context. Use one designated Windows profile for the field deployment; a different Windows account receives a different database and settings. Do not place the database on a shared folder for concurrent use.

## Authorization status

Operational use still requires the organization’s ISSM/ISSO, software approval, application-control/allowlisting, endpoint-security, records-management, data-location, backup-media, and physical-paper handling decisions. Review `SECURITY.md`, `THREAT-MODEL.md`, `STIG-READINESS.md`, and `AFNET-DEPLOYMENT-CHECKLIST.md`.
