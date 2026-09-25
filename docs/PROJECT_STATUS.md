# Equipment Tracking Platform project status

This is the living development handoff for the files currently in this folder. Update it when architecture, schema, release behavior, validated requirements, limitations, or priorities change.

## Current snapshot

The current verified build is **0.9.6-alpha.1 / MSI 0.9.6**, schema 6. The canonical Windows PowerShell release gate passed 154 tests (0 failed/skipped), validation, dependency audits, offline review, both publish modes, and MSI creation. The user authorized publication of source, AI handoff documents, and verified release assets as `v0.9.6-alpha.1` on 2026-09-25. No installation was performed. Evidence: `artifacts/validation/release-0.9.6-gate.log` and `VALIDATION.md`.

### 0.9.6 intake printing

- Intake now shows the successfully finalized ticket and **Print two 1297 copies** after the completion message. No Dashboard navigation is required. Reprinting does not re-finalize or modify the saved record.
- `IntakeCompletionViewModel` tracks the finalized transaction ID only; new/reset intake clears it. Busy and command reentrancy guards prevent overlapping jobs. The Intake presenter delegates to `TransactionDocumentService` and existing readable printing, then uses the existing prompt/deferred cleanup. Print failures leave the completion available for retry.
- Five new command-state regressions cover gating, correct record selection, reset/new-intake behavior, reprint availability, and overlapping clicks. The offscreen layout test loads the actual XAML completion panel and verifies ticket text, command binding, visibility, and action bounds. Synthetic print/service regression tests also pass. No PDF rendering or signature-verification implementation changed.

### 0.9.5 presentation fix

- `TransactionDocumentService` derives disposable copies from stored PDF paths plus committed device status. Dashboard and Equipment status current views show all returned devices, as does Dashboard printing; a selected receipt uses only its linked devices. Existing 0.9.4 records need no migration or PDF rewrite.
- `PrintJobService` shares the existing bold Arial layout between one-copy reference viewing and two-copy Letter printing, and draws explicit returned-device marks after the text on each copy. Signed originals, receipt bytes, database paths, and signature verification workflows stay unchanged. Derived copies preserve signature appearances, not digital-signature validity; unchanged evidence is available through Open preserved PDF.
- Documents now separates the cumulative current-status row from the last pickup receipt even when their source paths match. View selected, Print readable copies, and Open preserved PDF have distinct purposes. Temporary print cleanup is unchanged; temporary status views are cleared at startup by Maintenance.
- Four new automated cases cover cumulative/receipt/original/archive output, immutable source hashes, unchanged parent paths, temporary cleanup, invalid mappings, and unrelated documents. The dialog layout test now also exercises its minimum size. Synthetic PDF visual checks cover seven outputs and readable red cross-outs on both printed copies.
- Publication is authorized for this change as `v0.9.5-alpha.1`. Physical printer and Adobe/CAC acceptance remains outstanding; no installation or operational-data changes are authorized by publishing.

| Item | Current value or evidence |
|---|---|
| Verification date | 2026-09-25 (America/Denver); 0.9.6 canonical gate passed |
| Application version | `0.9.6-alpha.1` |
| File / assembly version | `0.9.6.1` / `0.9.6.0` |
| MSI product version | `0.9.6` |
| Database schema | `6` |
| Backup manifest format | `3` |
| Runtime | Windows x64, self-contained .NET 10 WPF |
| Development SDK | `10.0.110` selected by `global.json` |
| WiX SDK | `6.0.2` |
| Last complete release gate | `0.9.6-alpha.1` passed with `.\scripts\build-release.ps1 -AllowUnsignedPilotBuild`, exit code 0 |
| Source validation / automated tests | 154 passed, 0 failed, 0 skipped; standalone validation and the full release gate passed. |
| Release classification | `UNSIGNED SYNTHETIC-DATA PILOT ONLY`; `AuthenticodeSigned=false` |

## Repository and working-tree condition

- The currently opened folder is authoritative. Older archives and generated files are not a source baseline.
- On 2026-09-15, the user authorized publishing the complete current source, AI handoff documents, and verified 0.9.4 release to `https://github.com/LSmaroff/Equipment-Tracker`. This folder was initialized as a Git repository on `main`, retaining the existing remote history. Release tag: `v0.9.4-alpha.1`. Use `git status` and `git log -1` for current checkout state; generated output, private keys, local databases, and temporary files are excluded from source control.
- The 0.9.4 release transcript and complete final native-command output are retained under `artifacts/validation`; `release-0.9.4-final-gate.log` includes the test total. The manifest independently records `TestsSkipped=false`.
- Generated `bin`, `obj`, and `artifacts` trees are build output. Source JSON/XML validation intentionally excludes them.
- The 2026-08-10 remediation added missing `System.IO` imports to `AuthenticodeVerifier.cs` and `UpdateService.cs`, made `scripts/validate.ps1` insensitive to stale generated audit reports while retaining strict source/config validation, let release cleanup proceed past an Explorer-locked generated directory only when that directory is verified empty, and eliminated stale WPF `.g.cs` references by stopping and bypassing persistent build servers during release compilation.
- Release publishing now captures the offline verifier exit status immediately and stops before either publish mode when it is nonzero; repository validation enforces the fail-closed ordering.
- The customer-identity remediation makes New Intake name/rank authoritative through PDF preparation, finalization, SQLite, Dashboard, exports, and customer-derived filenames. The approved-template defaults now map customer to `ISSUED TO` and technician to `ISSUED BY`; legacy reversed defaults are repaired only for the approved template, and signer/certificate identity remains separate audit evidence.
- The `0.9.2-alpha.1` UX delta preserves spaces during progressive common-model-name editing and routes printable window input to a fresh Dashboard record search while Dashboard is loaded. Focused source tests and validation rules were added and the canonical 0.9.2 gate passed; physical keyboard/scanner/WPF-focus acceptance remains pending.
- The `0.9.3-alpha.1` presentation delta applies shared TextBox horizontal padding once while preserving vertical alignment and multiline stretching, uses Dashboard search padding `34,11,10,11`, and adds pie-slice pointer hit testing, a focus-colored hover outline, and category/count/percentage tooltip. The persistent labeled legend remains unchanged; automation name/help text flows through a peer, and weak collection notifications prevent navigation/refresh from retaining discarded chart visuals. The canonical gate passed; target pointer/theme/DPI/scanner acceptance remains pending.
- The current `0.9.4-alpha.1` source adds signed, device-linked subset-pickup receipts beneath one parent 1297. Each pickup copy derives from the immutable original signed intake, crosses only its selected device lines, fills `RETURN DATE`, and requires a newly verified `Pickup Signature` before SQLite marks those devices Returned. The parent remains active until no devices remain, then the final pickup archives it with all child documents. Schema 6 stores receipts and device links for idempotent commit and protected backup/restore. The canonical gate passed.
- The 0.9.4 scanner delta keeps the existing New Intake workflow and editable fields. It recognizes only the exact built-in `7ESQ7` / `2MQ5390WTS` identity as `A4TH1AV` / `HP EliteBook 645`, allows the exact operator catalog to override the model, and uses exact serial history only when the known part is unambiguous, nonblank historical model names do not conflict, and available CAGE evidence does not conflict. A history row with a known part but no friendly model may still supply the part; normal catalog/built-in/manual model resolution then applies. It preserves the exact raw scan and does not add a network dependency or a new intake toggle.
- Tests used synthetic records, PDFs, and backups, plus the supplied scanner sample as a regression fixture. No operational database or signed operational PDF was changed.

## Solution and directory structure

| Path | Purpose |
|---|---|
| `EquipmentTrackingPlatform.sln` | Contains the WPF application and xUnit test project. |
| `src/EquipmentTracking.App` | WPF views, view models, services, models, infrastructure, assets, settings, manifest, publish profile, and approved PDF template. |
| `tests/EquipmentTracking.Tests` | Unit and integration tests for parsing, migrations, backup/restore, recovery journals, PDF/template behavior, printing, record codes, themes, and release-readiness controls. |
| `installer/EquipmentTracking.Installer` | Separate WiX x64 per-machine MSI project; it is not part of the solution file. |
| `scripts` | Setup/build, validation, NuGet audit, offline review, publish, signing, and complete release automation. |
| `.vscode` | Restore/build/test tasks and the Debug launch configuration. |
| `artifacts/publish` | Conventional installed payload and portable single-file publish output. |
| `artifacts/installer` | Intermediate MSI output. |
| `artifacts/security` | Application/installer dependency inventory and vulnerability-audit JSON. |
| `artifacts/validation` | Offline-runtime review and other generated validation evidence. |
| `artifacts/release` | Versioned EXE/MSI, release manifest, and SHA-256 sums. |

## Supported development and deployment environment

- Verified build host: Microsoft Windows `10.0.26100`, x64, Windows PowerShell `5.1.26100.4652`.
- Application project: C#/.NET 10 WPF `WinExe`, `net10.0-windows`, `EnableWindowsTargeting=true`, `UseWPF=true`, source platform `AnyCPU`.
- Release output: `win-x64`, self-contained, untrimmed, no ReadyToRun. The portable variant is compressed single-file and self-extracts native/content payload; the MSI embeds a conventional multi-file payload.
- Installed runtimes observed on the build host include `Microsoft.NETCore.App 10.0.10` and `Microsoft.WindowsDesktop.App 10.0.10`.
- SQLite uses `Microsoft.Data.Sqlite.Core 10.0.10` plus `SQLitePCLRaw.bundle_winsqlite3 2.1.11`, which relies on the Windows-serviced `winsqlite3.dll`.
- WiX `6.0.2` builds an x64, English (`1033`), per-machine MSI under `ProgramFiles64Folder` with stable UpgradeCode `{C6534286-9C99-45F3-A3AF-F70508F03208}`.
- The main process requests `asInvoker` and writes per-user state. MSI installation/update legitimately uses administrator approval or an authorized deployment system.

## Application architecture

The application uses a WPF MVVM-style structure with manual service composition in `Infrastructure/AppServices.cs`.

- `App.xaml.cs` enforces one instance per signed-in user, initializes services, runs startup checks, and displays the main window.
- `AppServices` wires settings, theme, CAC discovery, barcode/record-code parsing, exact offline device recognition, PDF/signature services, SQLite, Excel export, backup scheduling, workflow journals, preflight, support packages, maintenance, printing, and updates.
- Views and view models implement Dashboard, New Intake, Equipment Status/returns, Recovery, Settings, How to use, closeout, diagnostics, preflight, and model-name prompting.
- `TransactionWorkflowService` owns intake preparation/finalization, signed-original preservation, device-scoped partial-pickup preparation/finalization, full-closeout organization archive placement, verified file movement, and resume/rollback behavior.
- `DatabaseService` owns transactional SQLite persistence, migrations, active-serial uniqueness, search, model catalog/history, pickup receipts and device links, audit history, file artifacts, and archive state.
- `WorkflowJournalService` persists durable JSON journals for intake finalization, partial pickup, and closeout stages.
- `BackupService` and `ScheduledBackupService` create and verify manual/full/differential backups, retention, staged restore, and next-startup application.
- `PdfFormService`, `SignatureExtractionService`, and `PrintJobService` fill the approved form, detect signature evidence/metadata, preserve signed sources, and create temporary two-copy Letter derivatives.
- `PreflightService`, `FileLogger`, and `SupportPackageService` provide startup readiness, bounded local logging, and sanitized diagnostics.
- `UpdateService` inspects a locally selected MSI and starts an attended `msiexec` update; it does not discover or download updates.

Startup order is: ensure local directories, apply any pending verified restore, load settings/theme, initialize and migrate SQLite, run maintenance and preflight, start the in-process backup scheduler, then route to Recovery when unfinished journals exist.

## Local data and database migrations

Application state is per Windows profile under:

~~~text
%LOCALAPPDATA%\58SOW\EquipmentTrackingPlatform
~~~

Default completed PDFs, Excel output, and backups are under `%USERPROFILE%\Documents\Equipment Tracking`. SQLite is designed for one workstation/user context and must not be used as a concurrent shared-drive database.

Schema migrations are numbered and recorded in `SchemaMigrations`:

1. Base Transactions, Devices, Audit, and Technicians tables.
2. Organization, ticket, rank, archive, and closeout columns.
3. FileArtifacts and WorkflowOperations tables.
4. Search/archive indexes, artifact deduplication, status normalization, and technician-history backfill.
5. Separate `PartNumber`, legacy backfill, and case-insensitive `ModelCatalog`.
6. Partial-pickup receipts and their device-number/device-ID links, with transaction indexes and foreign-key protection.

For an existing database, pending migrations normally trigger a verified pre-migration backup before changes. The application refuses a database newer than schema 6 and runs SQLite `quick_check` during initialization.

## Canonical commands

Run in Windows PowerShell from the repository root.

~~~powershell
# Restore, dependency audit, Debug build, and tests
.\scripts\build.ps1 -Configuration Debug

# Direct test command after restore/build when a focused rerun is appropriate
dotnet test .\EquipmentTrackingPlatform.sln --no-restore

# Static/semantic repository validation
.\scripts\validate.ps1

# Direct dependency and offline-runtime evidence
.\scripts\security-scan.ps1
.\scripts\verify-offline.ps1 -FailOnFinding

# Publish both self-contained application forms
.\scripts\publish.ps1

# Canonical complete unsigned developer/pilot release
.\scripts\build-release.ps1 -AllowUnsignedPilotBuild
~~~

Restore and `dotnet list package --vulnerable` require a reachable NuGet feed even with `--no-restore`. The current `NuGet.Config` uses public nuget.org; managed-network builds should pass an approved mirror through `-NuGetSource`. This is a build-time dependency, not a runtime Internet requirement.

## Release evidence

### Current verified release — 0.9.4-alpha.1

The source version, file/assembly versions, MSI fallback version, and schema are `0.9.4-alpha.1`, `0.9.4.1` / `0.9.4.0`, `0.9.4`, and 6 respectively. The canonical release command passed 145 tests (0 failed, 0 skipped), validation, application/installer dependency audits, offline-runtime review, conventional and portable publishing, and MSI creation. See the current section of `VALIDATION.md`, the final gate log, and `artifacts/release/release-manifest.json` for reconciled package evidence; historical 0.9.3 hashes below are not current-package hashes.

Synthetic validation also exercised unsigned-to-signed retry, post-commit recovery without its prepared source, successive pickups through final archive, pending-workflow exclusion, rollback preservation, backup/restore path remapping and missing-receipt rejection, and offscreen layout of the selection/signature/document screens. Rendered production-template fixtures confirmed selected-only marks on different pickup copies, unchanged original bytes, retained interactive/signature fields, and correct return dates. These are not physical scanner, Adobe/CAC, or installed-product acceptance tests.

The focused scanner parser/device-recognition subset passed 29 tests with 0 failed and 0 skipped on 2026-09-15. That focused result covers the reported exact HP scan and separator variants, raw/CAGE/serial preservation, negative identity cases, operator-catalog precedence, safe history ambiguity/CAGE rejection, and exact SQLite history lookup. It is not a substitute for the canonical gate.

### Prior verified release — 0.9.3-alpha.1

The canonical `.\scripts\build-release.ps1 -AllowUnsignedPilotBuild` gate passed on 2026-08-10 in 54.9 seconds. Its 112-test phase completed in 6 seconds with 0 failed and 0 skipped; repository validation passed; valid application and installer audits reported no vulnerable packages; the offline-runtime report contained no findings; conventional self-contained and portable single-file `win-x64` publishing passed; and WiX built the per-machine MSI.

Verified release files:

- `artifacts/release/EquipmentTrackingPlatform-0.9.3-alpha.1-win-x64-UNSIGNED-PILOT.exe` — 70,289,687 bytes; SHA-256 `7fc0b209c45404b955878a490f515510c249993e31dc696737bcb380735a5f61`.
- `artifacts/release/EquipmentTrackingPlatform-0.9.3-alpha.1-win-x64-UNSIGNED-PILOT.msi` — 58,154,645 bytes; SHA-256 `71550a1059d586829ae32e96d44787038abe2d4579734d66cca7568c3b606872`.
- `artifacts/release/release-manifest.json` — 1,246 bytes; SHA-256 `fa678706526cecfda633885ab673c6c718d1df0b944ea290d0598ab79b355830`.
- `artifacts/release/SHA256SUMS.txt` — 360 bytes; SHA-256 `7bcc9c6b7ad428d85f9e97f76e8e01213010ae623919037b1e9dadefb8c86a8d`.

The conventional publish audit verified 425/425 advertised payload hashes totaling 160,661,663 bytes; including its checksum file, the folder contains 426 files totaling 160,703,944 bytes. The portable EXE and `artifacts/installer/EquipmentTrackingPlatform-0.9.3-win-x64.msi` match the versioned release artifacts. The manifest was created at `2026-08-10T22:59:13.1198613-06:00` and records schema 5, `SelfContained=true`, `OfflineRuntime=true`, `PortableSingleFile=true`, `TestsSkipped=false`, `AuthenticodeSigned=false`, and `UNSIGNED SYNTHETIC-DATA PILOT ONLY`. MSI inspection records ProductCode `{B49660A9-9AA8-49C9-B4AD-BC29B5B87B7C}`, stable UpgradeCode `{C6534286-9C99-45F3-A3AF-F70508F03208}`, Manufacturer `58 SOW`, and `ALLUSERS=1`.

Required target acceptance covers light/dark themes at 100%, 125%, 150%, and 200% display scaling; Dashboard and representative single-/multiline TextBox layout; every representative pie slice, tooltip, pointer-leave/grouping/refresh/data-replacement state; continued access to the labeled legend; and a disposable `0.9.2` to `0.9.3` MSI upgrade/rollback.

### Prior verified release — 0.9.2-alpha.1

The final canonical `.\scripts\build-release.ps1 -AllowUnsignedPilotBuild` gate passed on 2026-08-10 in 56.7 seconds after the compiled help-text wording correction. All 103 tests passed with 0 failed and 0 skipped; repository validation passed; application and installer audits reported no vulnerable packages; the offline-runtime report contained no findings; conventional self-contained and portable single-file `win-x64` publishing passed; and WiX built the per-machine MSI. The conventional publish audit verified all 425 advertised payload hashes totaling 160,658,591 bytes; including the folder's `SHA256SUMS.txt`, it contains 426 files totaling 160,700,872 bytes.

Verified release files:

- `artifacts/release/EquipmentTrackingPlatform-0.9.2-alpha.1-win-x64-UNSIGNED-PILOT.exe` — 70,288,174 bytes; SHA-256 `6aec0fbc1427abf90dd0d2b78961eb13f2546e25d361c3a8f37c210c63667cf5`.
- `artifacts/release/EquipmentTrackingPlatform-0.9.2-alpha.1-win-x64-UNSIGNED-PILOT.msi` — 58,142,357 bytes; SHA-256 `6a307652a38e198561002ab59ac33883be777c8fc557d892eaed2763b6056b6d`.
- `artifacts/release/release-manifest.json` — 1,246 bytes; SHA-256 `6ca6b653e2be45dfab6f1e77253aa2c28b4a7b0f59b6eae1c6e4305c65aa21b6`.
- `artifacts/release/SHA256SUMS.txt` contains the matching hashes; the installer intermediate is `artifacts/installer/EquipmentTrackingPlatform-0.9.2-win-x64.msi` (58,142,357 bytes).

The manifest was created at `2026-08-10T22:18:58.0745959-06:00` and records application `0.9.2-alpha.1`, MSI `0.9.2`, schema 5, `SelfContained=true`, `OfflineRuntime=true`, `PortableSingleFile=true`, `TestsSkipped=false`, `AuthenticodeSigned=false`, and `UNSIGNED SYNTHETIC-DATA PILOT ONLY`. Physical scanner/WPF-focus timing and disposable installed-product upgrade/rollback acceptance remain pending and were not inferred from build-host evidence.

### Prior verified release — 0.9.1-alpha.1

The complete `0.9.1-alpha.1` unsigned release gate passed on 2026-08-10 after the customer-identity, PDF-mapping, versioning, and WPF build-server corrections. The earlier build-server correction was additionally verified with two consecutive `0.9.0-alpha.1` gates. The 0.9.1 gate:

- restored the application/test solution;
- compiled and passed all 89 automated tests;
- passed project structure, JSON/XML/XAML, semantic theme, WCAG contrast, focus, workflow, backup, migration, offline, template, printing, scanner, archive, organization, and PDF-mapping validation;
- produced valid application and installer dependency inventories/audits with no reported vulnerable packages;
- produced an empty offline-runtime findings report;
- published the conventional self-contained payload and portable single EXE;
- built the WiX MSI successfully; and
- created manifest and checksum evidence whose listed file sizes and SHA-256 values were independently rechecked.

Release files:

- `artifacts/release/EquipmentTrackingPlatform-0.9.1-alpha.1-win-x64-UNSIGNED-PILOT.exe`
- `artifacts/release/EquipmentTrackingPlatform-0.9.1-alpha.1-win-x64-UNSIGNED-PILOT.msi`
- `artifacts/release/release-manifest.json`
- `artifacts/release/SHA256SUMS.txt`

Additional outputs:

- conventional payload: `artifacts/publish/win-x64`
- portable publish: `artifacts/publish/win-x64-single-file/EquipmentTrackingPlatform.exe`
- installer intermediate: `artifacts/installer/EquipmentTrackingPlatform-0.9.1-win-x64.msi`
- audit evidence: `artifacts/security`
- offline report: `artifacts/validation/offline-runtime-review.json`

Unsigned output is the supported personal-development and pilot path; Windows `Unknown Publisher` is expected. The verified 0.9.1 manifest deliberately labels those files for synthetic-data pilot use. Code signing is optional and organization-controlled, not a prerequisite for local development.

## Established product requirements - actual status

| Requirement | Verified status in current implementation |
|---|---|
| C#/.NET WPF Windows desktop application | Implemented and compiled on the verified Windows x64 host. |
| Offline-first operation | Implemented. No listener, telemetry, cloud API, network updater, or runtime URL finding was found by the offline scan. Build-time NuGet access remains required. |
| Intake, active records, subset pickup, full closeout, Recovery, and searchable archive | Implemented through the WPF views/view models, workflow service, SQLite search, protected completed-record storage, and parent archive state. Real Adobe/CAC end-to-end use is unverified. |
| Intake customer identity | The name and rank entered in New Intake are authoritative for the approved template's `ISSUED TO` name row, saved transaction, Dashboard, exports, and final PDF filename. `ISSUED TO SIGNATURE` is the customer signature; a different signer is retained only in dedicated signer/certificate metadata. Automated coverage verifies the exact `MSgt Test T Testing` versus `MSgt Smaroff, Liam D` case. |
| Durable intake, partial-pickup, and closeout journals | Implemented with atomic JSON journals, pending/completed/failed/abandoned states, startup Recovery routing, resume, changed-attempt preservation, and guarded rollback. A committed pickup receipt is an irreversible boundary and cannot be rolled back. Real crash-point/Adobe integration is unverified. |
| SQLite migrations, backup/restore, preflight, single instance, logging, support packages | Implemented. Migration, backup/restore, clean preflight, and support redaction have automated coverage; hardware failure paths, multi-process activation, and logger rollover are unverified. |
| Preserve signed originals, pickup receipts, and later PDF states | Implemented with stable-read waits, temporary files, flush/close, SHA-256 verification, atomic placement, file-artifact/receipt records, and separate original/working/pickup/final/archive paths. Real signed Adobe documents are unverified. |
| CAC signatures as evidence and signer metadata | Implemented as intended: the app locates the configured signature field and extracts readable CMS/certificate identity/time metadata. It does not establish chain trust, revocation, or trusted timestamp validity. |
| Unique searchable 1297 ID | Implemented as `TX-yyyyMMdd-HHmmss-XXXXXXXX`; exact ID search covers active and archived transactions. |
| QR contains only dedicated lookup ID | Implemented as `ETP1297:<transaction-id>`; no customer/equipment fields enter the payload. PNG generation/parsing is tested. Physical printed scan is unverified. |
| QR lookup without click or Enter | While Dashboard is loaded, printable input from non-text, non-choice focus elsewhere in the application window starts a fresh search and moves focus to the search box. Focused text-entry controls retain native editing and ComboBox controls retain native type-selection. A complete record code submits immediately or remains pending until an in-progress Dashboard operation releases the search command, then resolves the exact transaction and switches active/archive scope. Pure routing and static lifecycle rules are covered; physical WPF focus/scanner timing remains unverified. Equipment Status continues to accept record codes for active-device lookup. |
| TextBox content alignment | The shared template applies horizontal padding once while preserving vertical alignment and multiline stretching. Dashboard search padding is `34,11,10,11` so typed text/caret align to its icon/placeholder. Structural regression coverage exists; visual theme/DPI acceptance remains pending. |
| Pie-chart hover identification | Pointer hit testing maps positive-value slices to a focus-colored hover outline and category/count/percentage tooltip, clears state on leave/resize/data changes/unload, and retains the labeled legend as the accessible non-hover and non-color-only equivalent. Automation properties are exposed through a peer, and weak collection notifications avoid retaining discarded charts. Geometry/lifecycle/accessibility regression coverage exists; physical pointer/theme/DPI acceptance remains pending. |
| Continuous intake scanner submission | Implemented with a 325 ms idle timer, scanner-speed/format detection, automatic submit, and focus advance. Enter remains a manual fallback. Real scanner timing is unverified. |
| Complete raw scan retention | Implemented for the supported 4096-character scan input: parsing uses a bounded value and SQLite stores/loads `RawScanValue`. |
| Supported scanner formats and exact HP identity | Parser and tests cover key/value and delimited input, ISO/IEC 15434 control separators/tokens, `17V`, `18S`, `30P`/`1P`, `S`, CAGE/serial extraction, and serial-before-part samples. Only the exact `7ESQ7` / `2MQ5390WTS` pair receives the built-in `A4TH1AV` / `HP EliteBook 645` identity; same-CAGE/different-serial and same-serial/different-CAGE cases do not. Additional physical scanner variants are unverified. |
| Part-number/model catalog and exact history recognition | Implemented with case-insensitive persistent operator mappings, exact built-in part/model data, conservative exact-serial history reuse, historical matching-device updates, and search/export support. Operator mappings override the built-in model. Conflicting parts, conflicting nonblank model names, or available CAGE conflicts reject history reuse; a known unambiguous part remains usable when the friendly model is blank. Progressive common-model editing retains internal spaces, and every auto-filled intake value remains editable. Unknown parts are not guessed. Focused recognition/model coverage exists; physical scanner and dialog interaction remain unverified. |
| Duplicate active serial protection | Implemented both in the intake view model and a normalized case-insensitive partial SQLite unique index; automated tests cover rejection and reuse after return. |
| Editable rows and Enter-as-Add | Implemented; committed intake rows remain editable with Add/Update state, Enter commits scan or row fields, and progressive multiword common-model typing is covered by an automated regression. Physical WPF UI interaction is unverified. |
| Two-up US Letter printing | Implemented and substantially tested: one portrait Letter page, two copies, center cut guide, copied signature appearances, source-hash immutability, and guarded temporary cleanup. Physical printer output is unverified. |
| Partial pickup, closeout, archive, and organization storage | Implemented. Each pickup prepares a child copy from the preserved original intake, crosses only selected device fields, fills the return date, verifies the exact pickup signature before status commit, and links the signed receipt to those devices. The parent stays active while devices remain and the last verified pickup archives it with every child receipt. Active and pickup PDFs remain below the configured Completed root; pickup children are grouped under `Pickup receipts/<transaction>`. A whole-record closeout places its final PDF under `Archived 1297s/<organization>`, while a parent completed by its last pickup points to that final signed receipt and remains grouped with all children through Documents. |
| Parent-record document access | Implemented through the Dashboard Documents action for active and archived parents, including original/current/final documents and each signed pickup copy labeled with device numbers and signer details. Physical Adobe opening is unverified. |
| Attended offline MSI update | Implemented with product/UpgradeCode/newer-version checks, optional manifest hash/size verification, offline Authenticode status, unfinished-journal block, verified pre-update backup, confirmation, UAC, and passive `msiexec`. Unsigned or missing-manifest packages warn but can be explicitly continued; actual install/rollback is unverified. |
| Sole approved scanner/tag emblem | Implemented in project and installer wiring: the approved ICO is used for the EXE/WPF window/MSI/ARP, and the matching PNG is used in main navigation. The lower dark scanner/tag PNG was visually confirmed; shortcut rendering on a target install is unverified. |
| Unsigned pilot support | Implemented. The explicit pilot flag is compatible with full tests/validation and emits clearly named `UNSIGNED-PILOT` files; `Unknown Publisher` is expected. |

## Important design and safety constraints

- Preserve the clean template filename, exact `Pickup Signature` field, no-JavaScript contract, `NeedAppearances=false`, and SHA-256 `00daa4ac652d592f03554e1d8e2ea9ec6083b30e9c9659330fe1dad32e599051`.
- The signed PDF is the authoritative signature evidence. Printing and later workflow stages must not replace or mutate signed originals.
- A partial pickup must derive from the immutable original signed intake, cross only the devices selected for that pickup, and verify a newly added signature in the exact `Pickup Signature` field before any selected device becomes Returned. A committed pickup receipt and its device links must remain idempotent and non-rollbackable.
- The QR payload must remain limited to the dedicated lookup ID.
- Active serial uniqueness is case-insensitive and trimmed.
- The backup scheduler runs inside the app: Monday 09:00 full and Monday-Friday 16:00 differential in local time, with next-launch catch-up and retry while open.
- Backups and restore are hash/integrity/schema/path checked. A differential requires its named full. Restore is staged and intentionally does not delete unrelated current PDFs.
- Keep database and settings per user, use approved ACL/encryption controls, and use a separate approved backup destination when drive-loss protection is required.
- Preserve the stable MSI UpgradeCode. A newer MSI must change one of the first three numeric fields; an alpha-suffix-only change is not a Windows Installer upgrade.
- Keep normal app execution non-administrative. Only the per-machine MSI install/update path may elevate.
- Never weaken NuGet vulnerability failures, offline checks, migrations, backup/restore, file-copy verification, or installer validation to pass a build.

## Known limitations, warnings, and incomplete work

- Branch, commit, diff, and uncommitted status are **unverified** because no Git metadata exists.
- This remains an alpha pilot, not DISA certification, AFNET approval, RMF authorization, an ATO, or a records-policy determination.
- The current 0.9.4 canonical release gate passed. Target-computer MSI install/upgrade/rollback, portable replacement, Adobe, CAC middleware/readers, physical Dashboard/intake/pickup scanner timing, printer output, endpoint controls, and physical paper handling remain **unverified** in this task.
- Physical light/dark-theme and 100%/125%/150%/200% DPI checks for TextBox caret/placeholder alignment, multiline stretching, pie-slice hover outlines/tooltips, and stale-hover clearing remain pending, as do disposable upgrade tests through the current `0.9.4` MSI.
- This build host currently has three related `0.9.0` MSI registrations from same-version rebuilds. No install/uninstall was authorized or performed here. Authorized disposable-target tests must still verify the historical `0.9.0` to `0.9.1` correction and the current `0.9.1` to `0.9.2` upgrade, remove older related products, preserve per-user data, and leave exactly one registration for the target version.
- There is no in-app role-based access control, shared multi-user database, centralized audit/SIEM integration, application-layer database encryption, backup encryption, or backup-manifest digital signature.
- Certificate-chain, revocation, and trusted-timestamp validation for PDF signatures are intentionally out of scope.
- The application-hosted backup scheduler cannot run while the application is closed or the user is logged off.
- The default backup folder is normally on the workstation drive and is not drive-loss protection by itself.
- The portable single-file EXE extracts required native/content files under the user temporary area.
- The in-app updater allows explicit continuation after an unsigned-package or missing-manifest warning. Identity/version mismatches and a present-but-mismatched manifest are refused; provenance policy remains an operator/organization responsibility.
- Current package reports show no vulnerabilities, but the installer audit does not enumerate `WixToolset.Sdk` as a NuGet package; independent WiX review and license/support acceptance remain required.
- Existing non-blocking analyzer warnings include `CA2011` in `StatusService`, `CA1806`/`CA1838` in MSI property reading, and performance/style findings in backup and print services.
- The xUnit v2 dependency is documented for a separately tested future migration.
- No build log/TRX, signed artifact, approved SAST/secret/malware scan, target field checklist, or operational authorization evidence is retained here.

## Current next-development priorities

1. Establish version control or another approved source-baseline mechanism, then retain a source identifier, build transcript, test results, audit reports, release manifest, and rollback artifact together.
2. Complete synthetic target-workstation acceptance for intake and partial-pickup scanner timing/samples, device-scoped pickup PDFs, multiple signed receipts, QR lookup, Adobe/CAC signatures, printing, schema-6 backup chains/restores, MSI install/upgrade, attended update rejection cases, and rollback.
3. Decide whether policy requires hard rejection of unsigned or manifest-less update packages; retain the current warning-confirmed path unless explicitly changed.
4. Test any future xUnit migration separately; do not combine it with product behavior work.
5. Complete and record synthetic target-workstation physical scanner/WPF-focus, partial-pickup selection/signature/document access, TextBox theme/DPI, chart-hover, and disposable upgrade acceptance from an earlier installed MSI to `0.9.4`.
6. Treat RBAC, logged-off backup execution, application/backup encryption, trusted PDF-signature validation, smart-card modernization, and centralized audit as explicit future requirements only if approved; do not infer them into the current architecture.

## Decision log

- **2026-08-10:** Declared the currently opened working folder authoritative; older archives and remembered code are not valid replacement sources.
- **2026-08-10:** Kept unsigned, clearly labeled pilot builds as the canonical personal-development release path; code signing remains optional and organization-controlled.
- **2026-08-10:** Corrected missing `System.IO` imports that blocked compilation without changing application behavior.
- **2026-08-10:** Limited source JSON/XML validation to non-generated repository files so a failed audit report cannot poison the next release rerun; malformed source/config files still fail with their actual path.
- **2026-08-10:** Kept release cleanup strict while allowing an Explorer-locked generated directory to remain only when post-cleanup enumeration proves it empty; any residual entry remains a hard failure.
- **2026-08-10:** Made consecutive release runs deterministic by shutting down persistent .NET build servers before WPF `bin`/`obj` cleanup and disabling server reuse for restore, test, both publish modes, and MSI compilation.
- **2026-08-10:** Verified two immediate back-to-back complete unsigned release gates; the second run regenerated the missing-prone Debug/Release `App.g.cs` and `DashboardView.g.cs` files after cleanup and passed all artifact checksum checks.
- **2026-08-10:** Verified the complete unsigned release gate with 87 passing tests, validation, audits, offline review, both publish modes, and MSI creation.
- **2026-08-10:** Added root continuity instructions and this living handoff; corrected the VS Code launch target and replaced the obsolete non-Windows validation limitation with current Windows evidence.
- **2026-08-10:** Made the New Intake name/rank authoritative during finalization; PDF signer identity remains separate audit evidence and can no longer replace the customer record.
- **2026-08-10:** Verified the approved template's `ISSUED TO` and `ISSUED BY` labels/widgets, corrected the reversed signature defaults and fallback, repaired legacy approved-template settings, and filled the Issued-To name row from the entered customer.
- **2026-08-10:** Promoted the corrected package to application `0.9.1-alpha.1` / MSI `0.9.1` so Windows Installer sees it as newer than installed `0.9.0` products; the stable UpgradeCode, schema 5, and same-version-upgrade prohibition remain unchanged.
- **2026-08-10:** Preserved progressive spaces in New Intake common-model editing while retaining normalization at Add/Update and persistence boundaries.
- **2026-08-10:** Added Dashboard-wide printable type/scan-to-search with focused-control/shortcut guards, unload detachment, complete-code source synchronization, busy-search deferral, and focused routing/static regression coverage.
- **2026-08-10:** Verified application `0.9.2-alpha.1` / MSI `0.9.2` with the final 56.7-second canonical unsigned release gate: 103 tests passed, validation/audits/offline review, both publish modes, MSI creation, manifest, and checksums passed. The resulting files are restricted to synthetic-data pilot use; physical scanner/WPF-focus and disposable upgrade acceptance remain pending.
- **2026-08-10:** Made release publishing fail closed by capturing the offline verifier exit status and stopping before either publish mode on a nonzero result; repository validation now enforces this order.
- **2026-08-10:** Verified application `0.9.3-alpha.1` / MSI `0.9.3` with the final hardened 54.9-second canonical unsigned release gate: its 112-test phase passed in 6 seconds with 0 failed/skipped, and validation/audits/offline review, both publish modes, MSI creation, manifest, and checksums passed. The files are restricted to synthetic-data pilot use; physical pointer/theme/DPI/scanner and disposable upgrade acceptance remain pending.
- **2026-09-15:** Verified application `0.9.4-alpha.1` / MSI `0.9.4` / schema 6 with 145 passing tests and the complete canonical unsigned release gate. Added device-scoped signed partial pickups, parent/child document history, guarded recovery/idempotent commit, protected backup/restore, and exact offline HP recognition while retaining the check-in workflow, whole-record closeout, and ordinary status editing.
- **2026-09-15:** Added exact offline recognition for the reported `18S` CAGE `7ESQ7` / serial `2MQ5390WTS` sample as `A4TH1AV` / `HP EliteBook 645`, with operator-catalog precedence and conservative exact-history reuse. The existing New Intake flow, editable values, duplicate checks, and exact raw scan retention remain unchanged; the focused 29-test parser/recognition subset passed.
