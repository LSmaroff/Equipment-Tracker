# Changelog

## 0.9.6-alpha.1

- Added **Print two 1297 copies** directly in Intake after successful finalization. The completed ticket is displayed beside the action; no Dashboard navigation is needed.
- Uses the existing readable two-copy Letter layout and print dialog, with immutable signed originals and prompt/deferred temporary-file cleanup. Reprinting is supported without re-finalizing; a print error does not undo the intake.
- Starting or resetting an intake clears the previous completed print target. Unsigned/prepared forms cannot enable this action, and busy/double-click guards prevent overlapping print jobs.
- MSI version advances to 0.9.6 for upgrades; schema remains 6. Added completion-state/command regressions and a rendered check of the actual Intake completion panel.

## 0.9.5-alpha.1

### Fixed

- Dashboard and Equipment status PDF opening now derive a readable current-status copy from the stored PDF and committed device status, crossing out all completed pickups. Previously these actions opened the unchanged parent PDF, hiding partial pickups.
- Dashboard printing includes the same cumulative cross-outs on both readable Letter copies. Archived parents include all returned devices, not just the final receipt's subset.
- Documents now offers readable viewing and two-copy printing for each signed pickup, using only that receipt's linked devices. A separate current-status row remains available even when its source is the final pickup PDF. Open preserved PDF still opens unchanged signature evidence.
- Generated reference copies retain signature appearances, but are not digitally signed replacements. All source PDF bytes, database paths, pickup links, and backups remain unchanged; 0.9.4 receipts work without data migration. Temporary views are cleaned at application startup, and print sheets retain prompt/deferred cleanup.
- MSI version advances to 0.9.5 for upgrades from 0.9.4; schema stays at 6. Automated cumulative/subset/original/archive, failure, source-preservation, cleanup, and dialog-layout regressions accompany the fix.

## 0.9.4-alpha.1

### Added

- An active 1297 can now record one or more partial pickups. The operator selects exact devices by scan or checkbox, can select all devices already marked Ready for pickup, and can clear the selection before preparing the pickup copy.
- Each pickup uses a separate child copy derived from the preserved original signed intake. Only the devices in that pickup are crossed out, `RETURN DATE` is filled, and the customer signs the exact `Pickup Signature` field. No selected device is marked Returned until that signature is verified.
- Signed pickup receipts and their device links are retained under the original parent 1297. The Dashboard Documents action lists the original/current/final documents and every signed pickup copy with its device numbers and signer details.
- SQLite schema 6 adds the partial-pickup receipt and device-link records needed for idempotent commit, search/display, recovery, backup, and restore.
- Intake scanner recognition now preserves the exact `18S` CAGE and serial components internally. The exact `7ESQ7` / `2MQ5390WTS` identity resolves offline to part `A4TH1AV` and model `HP EliteBook 645`; it does not guess from the CAGE or serial alone. Exact operator catalog entries take precedence, and prior exact-serial history is reused only when its known part is unambiguous, its nonblank model names do not conflict, and any available CAGE evidence does not conflict.

### Changed

- Application version advanced to `0.9.4-alpha.1`, file/assembly versions to `0.9.4.1` / `0.9.4.0`, MSI product version to `0.9.4`, and SQLite schema to version 6. The stable MSI UpgradeCode remains unchanged.
- A parent 1297 remains active after a signed subset pickup while any devices remain open. Verifying the last pickup returns the final devices and automatically archives the one parent record with all of its child pickup documents.
- The existing full-closeout path and ordinary device-status editing remain available. When a dialog contains both pickup selections and ordinary status edits, pickup signature verification completes before the ordinary updates are applied.
- Partial-pickup recovery can resume or abandon an incomplete attempt, preserves changed attempts as evidence, and refuses to roll back a receipt after its database commit.

### Verification

- The canonical `0.9.4-alpha.1` release gate passed all 145 tests, with 0 failed and 0 skipped, plus repository validation, dependency audits, offline-runtime review, both EXE publish modes, and WiX MSI creation. Artifacts remain unsigned synthetic-data pilot output; see `VALIDATION.md` for release evidence.
- Added synthetic workflow regressions for unsigned-sign-resume, recovery after commit without the prepared source, successive subset pickups through final archive, pending-workflow guards, and evidence-preserving rollback. Offscreen WPF layout checks and rendered production-template pickup fixtures verify the new screens and selected-only PDF marks without operating on live records.
- The focused parser/device-recognition subset passed 29 tests with 0 failed and 0 skipped, including the exact field sample, scanner separator variants, CAGE/serial negative cases, operator-catalog precedence, history ambiguity/collision rejection, and exact SQLite history lookup.

### Preserved

- New Intake remains the same scan-and-add workflow with editable part/model/serial fields and active-serial duplicate prevention; there is no new intake checkbox or required network lookup.
- The entered customer identity, signed original intake, existing full closeout, ordinary status editing, printing, exports, backup/restore safeguards, stable MSI UpgradeCode, offline runtime, and unsigned synthetic-data pilot option remain intact.

## 0.9.3-alpha.1

### Changed

- Application version advanced to `0.9.3-alpha.1`, file/assembly versions to `0.9.3.1` / `0.9.3.0`, and MSI product version to `0.9.3`; SQLite schema remains version 5 and the stable MSI UpgradeCode is unchanged.
- Hovering a Dashboard device-distribution pie slice now draws a visible focus-colored outline and shows its category, device count, and percentage. The chart exposes its automation name/help text through a peer, uses weak collection notifications so navigation/refresh cannot retain discarded chart visuals, and keeps the persistent labeled legend available without pointer hover.
- Release publishing now captures the offline verifier exit code immediately and stops before either publish mode on any verifier failure; repository validation enforces this fail-closed ordering.

### Fixed

- The shared TextBox template now applies each control's padding once, aligning the caret and typed text with the intended content inset instead of shifting them too far right. This includes the Dashboard search text/placeholder alignment.

### Verification

- The canonical `.\scripts\build-release.ps1 -AllowUnsignedPilotBuild` gate completed in 54.9 seconds with 112 tests passed, 0 failed, and 0 skipped; repository validation, dependency audits with no reported vulnerable packages, an empty offline-runtime findings report, both publish modes, WiX MSI creation, and manifest/checksum verification passed.
- The verified unsigned pilot artifacts are `EquipmentTrackingPlatform-0.9.3-alpha.1-win-x64-UNSIGNED-PILOT.exe` (70,289,687 bytes; SHA-256 `7fc0b209c45404b955878a490f515510c249993e31dc696737bcb380735a5f61`) and the matching MSI (58,154,645 bytes; SHA-256 `71550a1059d586829ae32e96d44787038abe2d4579734d66cca7568c3b606872`).
- Nine facts added over the prior 103-test baseline cover shared single-horizontal-padding/vertical-alignment structure, multiline host stretching, pie hit testing, chart automation text through its peer, and weak collection-subscription cleanup after unload.

### Preserved

- The accessible chart legend, all search behavior, customer/signature semantics, database schema and data, backup/restore behavior, signed-PDF preservation, recovery journals, stable MSI UpgradeCode, offline runtime, printing, exports, and unrelated workflows remain unchanged.

## 0.9.2-alpha.1

### Changed

- Application version advanced to `0.9.2-alpha.1`, file/assembly versions to `0.9.2.1` / `0.9.2.0`, and MSI product version to `0.9.2`; SQLite schema remains version 5.
- While Dashboard is open, printable typing or scanner input from elsewhere in the application window starts a fresh record search and moves focus to the Dashboard search box. Complete printed 1297 record codes continue to submit automatically, including when a Dashboard refresh temporarily makes search unavailable.

### Fixed

- Progressive typing in the New Intake common-model field now preserves the space between words instead of trimming the in-progress trailing space. Surrounding whitespace is still normalized when the device row is added or updated.
- Dashboard scanner input no longer requires the operator to click the search box first, and repeated navigation does not leave duplicate window-level input handlers attached.

### Verification

- Standalone validation and the canonical `0.9.2-alpha.1` unsigned release gate pass. The final gate completed in 56.7 seconds with 103 tests passed, 0 failed, and 0 skipped; clean validation, dependency audits with no reported vulnerable packages, an empty offline-runtime findings report, both EXE publish modes, WiX MSI creation, and matching manifest/checksum evidence.
- The verified unsigned pilot artifacts are `EquipmentTrackingPlatform-0.9.2-alpha.1-win-x64-UNSIGNED-PILOT.exe` (70,288,174 bytes; SHA-256 `6aec0fbc1427abf90dd0d2b78961eb13f2546e25d361c3a8f37c210c63667cf5`) and the matching MSI (58,142,357 bytes; SHA-256 `6a307652a38e198561002ab59ac33883be777c8fc557d892eaed2763b6056b6d`).

### Preserved

- Customer/signature semantics, database schema and data, backup/restore behavior, signed-PDF preservation, recovery journals, stable MSI UpgradeCode, offline runtime, printing, exports, and all unrelated workflows remain unchanged.

## 0.9.1-alpha.1

### Changed

- Application version advanced to `0.9.1-alpha.1`, file/assembly versions to `0.9.1.1` / `0.9.1.0`, and MSI product version to `0.9.1`; SQLite schema remains version 5.
- The numeric MSI version increase makes this package a strictly newer major-upgrade candidate for installed `0.9.0` products while retaining the stable UpgradeCode and same-version-upgrade prohibition.
- The approved template's `ISSUED TO: NAME, GRADE, ORGN` row is now filled from the customer name/rank entered in New Intake rather than the technician field.
- Default signature mappings now follow the form labels: customer/recipient is `ISSUED TO SIGNATURE`, and technician/issuer is `ISSUED BY SIGNATURE`. Existing settings containing the former reversed defaults are corrected when the approved template loads; custom-template mappings remain untouched.

### Fixed

- Finalization preserves `MSgt Test T Testing` (displayed as `MSgt Testing, Test T`) as the customer even when the PDF signer is `MSgt Smaroff, Liam D`; signer and certificate identity remain separate audit metadata and cannot replace the entered customer.
- Release validation now requires coherent application, file, assembly, and fallback MSI versions so a rebuilt package cannot silently retain an older Windows Installer identity.

### Preserved

- Database schema, per-user data, backup/restore behavior, signed-PDF preservation, recovery journals, stable MSI UpgradeCode, offline runtime, printing, scanning, exports, and all existing workflow features remain unchanged.

## 0.9.0-alpha.1

### Added

- Unique printed 1297 IDs and QR codes using the existing transaction ID, with exact active/archive lookup from Dashboard and Equipment Status scanner input.
- Scanner-idle submission for intake rows and printed record searches, while retaining Enter as a manual fallback.
- Field-sample coverage and normalization for ISO/IEC 15434 variants, literal control tokens, missing header characters, manufacturer prefixes, 17V/18S, and serial-before-part layouts.
- Persistent part-number-to-common-model catalog, automatic model reuse, post-finalization prompts for unknown models, historical device updates, search, audit, PDF, and Excel support.
- Offline **Install update package…** workflow with MSI ProductName/UpgradeCode/version checks, optional companion-manifest SHA-256/size verification, unfinished-work blocking, pre-update backup, and elevated Windows Installer launch.
- QRCoder 1.8.0 for dependency-free offline PNG QR generation.

### Changed

- Application version advanced to `0.9.0-alpha.1`; SQLite schema advanced to version 5.
- Device records now store `PartNumber` separately from the friendly `Model` name; migration 5 safely backfills existing part numbers from the former Model field.
- Generated PDF device blocks and Excel exports now show model name and part number as separate values.
- Release builds now stop persistent .NET build servers before deleting WPF intermediates and disable server reuse throughout restore, test, publish, and MSI compilation, preventing reruns from referencing deleted generated `.g.cs`/BAML files. Cleanup still tolerates a Windows-locked directory only after verifying it is empty; any remaining build state fails the release.

### Fixed

- Finalizing a 1297 now keeps the customer name and rank entered during intake, even when a different person signs the PDF; signer and certificate details remain recorded separately as audit metadata.

### Preserved

- Stable MSI UpgradeCode, data locations, clean template hash, signed-PDF preservation, two-copy Letter printing, release signing controls, black application icon, and the previously validated MSI shortcut corrections.

## 0.8.0-alpha.1

### Added

- In-application backup scheduler: full Monday at 09:00 and differential Monday through Friday at 16:00, using local workstation time with latest-missed-run catch-up, including prior-Friday catch-up before 09:00 Monday.
- Scheduled full archives containing a consistent SQLite snapshot, settings, and every PDF below the completed-record root.
- Differential archives containing a complete SQLite snapshot, PDFs new or changed since the current weekly full, and markers for paths moved/deleted since that full.
- ZIP, SHA-256, settings/PDF hash, and SQLite integrity verification before a scheduled archive is accepted; `.sha256` sidecars accompany scheduled backups.
- Differential-aware staged restore, Monday-full discovery, verified PDF merge, destination path remapping, and pre-restore safety copies.
- Retention that independently re-verifies the next full before removing prior-week differentials and retains a configurable count of weekly fulls.
- Backup creation guards for missing/outside/non-PDF official database paths and child reparse points.
- Restore-manifest guards for Windows device names, alternate data streams, trailing-dot/space names, invalid base-full names, and official files that disappear during backup preparation.
- Backup-destination readiness checks for write access, path overlap, same-drive/share risk, FAT32, and low free space; the startup template check now enforces the approved SHA-256.
- Visible footer/Settings status for scheduled-backup success or failure, with 15-minute retry guidance.
- Self-contained portable Windows x64 single EXE release profile.
- WiX 6.0.2 machine-wide MSI with embedded conventional multi-file payload, stable major-upgrade identity, rollback-safe replacement scheduling, same-version refusal, downgrade blocking, and shortcuts.
- Offline release build, application/installer dependency audits, signing, checksum, and manifest automation plus deployment and field-test guides; unsigned output now requires an explicit pilot flag and receives `UNSIGNED-PILOT` file names.
- Signed field candidates cannot use the test-skip switch; test-skipped builds are restricted to explicitly marked unsigned synthetic-data pilots and record that state in the release manifest.
- Automated coverage for full/differential creation and restore, deletion overlays, official-path refusal, corrupt-full retention preservation, and schedule timing.

### Changed

- The field-build baseline is pinned to .NET SDK 10.0.110 / runtime 10.0.10; ClosedXML is updated to 0.105.1, Microsoft.Data.Sqlite.Core to 10.0.10, and Microsoft.NET.Test.Sdk to 18.8.1.
- Manual **Create backup** is now **Create full backup now** and protects database, settings, completed PDFs, preserved originals, and archived records.
- The backup retention setting now describes weekly fulls; prior-week differentials are lifecycle-managed separately.
- Restore verifies staged settings and preserves current PDFs not present in the selected backup.
- Scheduled backup manifest format is version 3 so differential deletion markers can be represented; older application builds may refuse these newer archives.
- Application assembly/release name is `EquipmentTrackingPlatform`; application version is `0.8.0-alpha.1`. SQLite schema remains version 4.
- Offline updating uses a newer signed MSI or portable EXE through the approved distribution process; no runtime network updater was added.

### Operational note

- Exact scheduled execution requires the application to be open. When it was closed, the latest missed backup catches up at next launch. Unattended execution while logged off would require a separately approved service/task design.

## 0.7.2-alpha.1

### Changed

- Every AcroForm text field is now redrawn only in the temporary print derivative using embedded bold Arial instead of preserving thin or inconsistently spaced viewer appearances.
- Field-aware layout keeps dates, quantities, ticket values, fixed form labels, header values, and device identifiers aligned within their original boxes with consistent padding.
- Text is normalized for print presentation, wraps at safe boundaries, and reduces in half-point steps only when needed; printing stops with the affected field name if a complete value cannot fit above the readability floor.
- Device blocks re-establish separate Part Number, Serial Number, and Asset Tag paragraphs even when an older PDF appearance visually ran them together.
- Signature widgets continue to use their exact existing appearance streams; no signature content is retyped or altered.
- Application version advanced to `0.7.2-alpha.1`; the SQLite schema remains version 4.

### Preserved

- The selected active/archive PDF, signed original, canonical AcroForm values, digital-signature appearances, database path, and archive record remain unchanged.
- Two-copy Letter geometry, center cut guide, source SHA-256 guard, print-notice cleanup, offline behavior, and NuGet dependency set remain unchanged.

## 0.7.1-alpha.1

### Changed

- Renamed the Dashboard action from **Print 2 copies** to **Print 1297's**.
- Device Part Number, Serial Number, and Asset Tag text is redrawn only in the temporary print derivative using embedded bold Arial.
- Device text preserves explicit line breaks, wraps at word, hyphen, slash, or character boundaries, and selects the largest font size that keeps the complete value inside the approved field box.
- The application now displays one printing notice for both Adobe and default-viewer paths. The print derivative stays available while that notice is open and is deleted when the operator closes it.
- Locked print files receive short immediate retries, up to two minutes of background retry, and cleanup on the next startup if the viewer still holds them.
- Application version advanced to `0.7.1-alpha.1`; the SQLite schema remains version 4.

### Preserved

- The selected active/archive PDF, signed original, field values, digital-signature appearance, database path, and archive record remain unchanged.
- Two-copy Letter geometry, the center cut guide, source SHA-256 guard, offline behavior, and NuGet dependency set remain unchanged.

## 0.7.0-alpha.1

### Added

- Dashboard **Print 2 copies** action for both active and archived 1297 records.
- One-page portrait US Letter imposition with two 8.5-by-5.25-inch copies and a dashed center cut guide.
- Visual duplication of AcroForm and signature appearance streams without changing the selected operational PDF.
- SHA-256 source-integrity verification before and after print-sheet generation.
- Adobe `/p` print-dialog launch with a safe default-PDF-viewer fallback.
- Local `PrintJobs` cache, readiness check, configured temporary-file retention, and pilot reset cleanup.
- Automated tests for Letter geometry, duplicated printable appearances, form-field preservation, wrong-layout rejection, and source immutability.

### Changed

- Dashboard actions now provide Open PDF, Print 2 copies, and Close out 1297 in one selection-aware action row.
- The local operator guide, security notes, threat model, validation, and AFNET pilot checklist now cover temporary paper-copy printing.
- Application version advanced to `0.7.0-alpha.1`; the SQLite schema remains version 4.

### Preserved

- Active/archive PDFs, preserved signed originals, signature evidence, database paths, and archive records are never replaced by a print job.
- No runtime network, telemetry, updater, or new NuGet dependency was added.

## 0.6.0-alpha.1

### Added

- Centralized semantic design tokens for canvas, layered surfaces, inputs, hover, selection, focus, text hierarchy, borders, status colors, shape, spacing, and control states.
- Persistent active-page treatment and one consistent Fluent icon family throughout the primary navigation and actions.
- Purpose-built loading, empty, no-result, and no-selection states for Dashboard, Equipment Status, Recovery, and intake device capture.
- A stable four-step intake map and current-stage summary that preserve context during scanner and Adobe workflows.
- Visible keyboard focus treatment, accessible control names, descriptive tooltips, and text alternatives for the Dashboard chart.
- Automated static checks for semantic resource completeness and WCAG AA color contrast in the development package.

### Changed

- Reworked every main view and dialog around a shared 4-pixel-based spacing rhythm, 40-pixel controls, moderate radii, restrained borders, and consistent typography.
- Rebalanced Dashboard hierarchy so active equipment is primary while transaction counts, pickup activity, archive totals, search, records, and distribution remain easy to scan.
- Made Settings use one stable Save action while separating backup, diagnostics, retention, PDF mapping, and destructive pilot-reset actions.
- Replaced repeated operator-guide cards with one readable workflow and separate Recovery and support guidance.
- Replaced the always-green global status indicator with a neutral informational state; success, warning, and danger colors are reserved for meaningful status.
- Corrected New Intake guidance to state that the application fills `DATE OF ISSUE`, `RETURN DATE`, and `QNTY` because the approved template contains no JavaScript.
- Updated the color-blind-friendlier chart palette while retaining labeled values so meaning is never color-only.
- Application version advanced to `0.6.0-alpha.1`.

### Preserved

- Scanner-first Enter-to-add/update behavior, editable committed device rows, raw-scan retention, active-serial protection, active-card-only CAC discovery, signature detection, closeout, archive, Recovery, backup/restore, schema version 4, and all 0.5.0 release-readiness controls.
- Exact `Pickup Signature` mapping and the approved clean-template SHA-256.
- Offline runtime policy and standard-user operation.

## 0.5.0-alpha.1

### Added

- Durable, resumable intake-finalization and closeout workflow journals.
- Recovery page with resume, open-PDF, and safe rollback actions.
- Per-user single-instance protection and activation of the existing window.
- Verified SQLite backup and staged restore with manifests and SHA-256 validation.
- Numbered database migrations, schema-version checks, and automatic pre-migration backups.
- Startup readiness checks for storage, SQLite, the 1297 template, PDF application, smart-card services/readers, and disk space.
- Sanitized support-package creation without automatic database or operational-PDF inclusion.
- Detailed Pickup Signature diagnostics.
- File-artifact records and preservation of original intake, working/status, final closeout, and changed abandoned-closeout PDFs.
- Configurable retention for logs, temporary files, completed recovery journals, and backups.
- Disabled-by-default, non-administrator pilot/test reset with phrase confirmation and verified backup.
- Local How to use operator guide.
- Offline-runtime verification script.
- Release-signing script for organization-provided certificates.
- Integration tests for migrations, backup/restore integrity, recovery journals, template cleanliness, signature extraction, preflight, support-package redaction, and test-data reset.

### Changed

- Application version advanced to `0.5.0-alpha.1`.
- Cleaned 1297 template is included and used by default.
- The application fills `DATE OF ISSUE`, `RETURN DATE`, and `QNTY`; the PDF contains no JavaScript.
- File copy/archive operations wait for stable access, retry transient locks, verify hashes, and atomically rename temporary files.
- Startup routes directly to Recovery when an interrupted workflow exists.
- Support diagnostics replace local user/machine/path details with sanitized identifiers/placeholders.
- Database restore is reverified immediately before it replaces the current database.

### Deferred or external

- MSI installer creation is intentionally deferred.
- Production code signing requires an organization-approved certificate and private-key handling outside the repository.
- AFNET allowlisting, endpoint security, Adobe/CAC middleware validation, records policy, and authorization remain organizational activities.

## 0.4.0-alpha.1

### Added

- Dashboard closeout workflow for active 1297s.
- Searchable Dashboard archive separate from active transactions.
- Organization-specific archive folders under `Archived 1297s`.
- Strict Pickup Signature detection before closeout finalization.
- SHA-256-verified PDF archive copy before database commit and source deletion.
- Closeout technician, preparation time, close time, archive state, and audit records.
- Customer rank field, rank catalog, rank parsing, and rank-aware customer display.
- Active-card-only CAC discovery through Windows smart-card reader state and card certificate stores.
- Support for displaying one best certificate from each of multiple inserted readers/cards.
- Enter-key Add/Update behavior for all device-entry text boxes.
- Editable committed device-entry rows before PDF preparation.
- Build-time direct/transitive NuGet audit and security report.
- Publish-output SHA-256 manifest.
- Security, STIG-readiness, threat-model, and AFNET/NIPR deployment-review documentation.

### Changed

- Transaction IDs now use an eight-character GUID-derived suffix instead of a 16-bit random suffix.
- Active serial-number uniqueness is case-insensitive and ignores surrounding spaces.
- Selecting a CAC without rank preserves a manually entered rank.
- Raw scan data remains in SQLite but is omitted from Excel exports.
- SQLite uses `Microsoft.Data.Sqlite.Core` plus `SQLitePCLRaw.bundle_winsqlite3`, relying on Windows-serviced `winsqlite3.dll` instead of bundling `e_sqlite3`.
- Build and publish scripts invoke the dependency audit.
- Manifest declares Windows 10/11 compatibility and continues to request `asInvoker`.
- Application fallback version updated to 0.4.0.

### Security hardening

- Parameterized SQLite values and escaped wildcard searches retained.
- `trusted_schema=OFF`, foreign keys, busy timeout, transactions, and uniqueness constraints retained.
- Smart-card enumeration no longer reads the cached `CurrentUser\My` store.
- CAC private keys and PINs are not accessed.
- File and directory components are normalized and length-limited.
- Log messages are bounded and control-character sanitized; stack traces and sensitive certificate/raw-scan contents are not logged.
- PDF signature reading is capped at 100 MB and signature-locator regular expressions have timeouts.
- Known NuGet vulnerability warnings `NU1901`–`NU1904` are treated as errors.

### Important limitations

- This release is not a DISA certification, AFNET approval, or Authority to Operate.
- The application remains a single-workstation alpha with no in-application role-based access control.
- The SQLite database is not application-layer encrypted and must rely on approved endpoint encryption and ACLs.
- Signature trust, revocation, and trusted timestamp validation remain intentionally out of scope.
- Windows WPF compilation and real Adobe/CAC hardware testing must be completed on the target Windows environment.

## 0.3.2-alpha.1

- Corrected dark-theme ComboBox popup and item colors.
- Made Equipment Status results read-only and double-click open the selected PDF.
- Moved transaction-level device update to Equipment Status.
- Corrected displayed status refresh.
- Expanded DoD IUID parser handling for compact and visible separator forms.

## 0.3.0-alpha.1

- Added dark/light/system themes, grouped Dashboard search, ticket numbers, charting, technician history, active serial protection, Equipment Status, and PDF device strike-through workflow.

## 0.2.x

- Added configured 1297 field mappings, continuous scan rows, organization dropdowns, SQLite/Excel storage, PDFsharp WPF support, restore/build fixes, and retry-safe finalization.

## 0.1.0-alpha.1

- Initial WPF alpha.
