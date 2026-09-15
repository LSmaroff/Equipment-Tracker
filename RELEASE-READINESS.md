# Release-readiness implementation — 0.9.3-alpha.1

## Implemented in source

1. Durable intake and closeout journals with idempotent resume and protected rollback.
2. Per-user single-instance enforcement.
3. Verified legacy/manual backup plus scheduled weekly full and weekday differential backup, including moved/deleted-PDF markers.
4. Full protection of SQLite, settings, completed PDFs, preserved originals, and archived 1297s, with database-to-record-root validation before acceptance.
5. Staged restore with ZIP/hash/SQLite verification, differential-base resolution, path remapping, and pre-restore safety copies.
6. Retention that independently re-verifies the new full before deleting prior-week differentials and retains a configurable count of weekly fulls.
7. Numbered database migrations, automatic pre-migration backup, and newer-schema refusal.
8. Startup readiness checks for local data, output paths, backup destination, disk format/space, SQLite, template, Adobe, and smart-card readers.
9. Sanitized support packages, bounded logs, and signature diagnostics.
10. Offline runtime with no network updater, telemetry, API, listener, or external-template dependency; the attended Settings command only launches a locally supplied verified MSI.
11. One-file portable Windows x64 EXE and one-file machine-wide MSI release choices.
12. MSI major-upgrade identity, rollback-safe replacement scheduling, same-version refusal, downgrade blocking, embedded payload, and Start/Desktop shortcuts.
13. SHA-256 release manifests, organization-controlled Authenticode signing hooks, and default refusal to produce ambiguously named unsigned release artifacts.
14. Verified PDF copy/placement, preserved signed versions, recovery, and non-destructive paper-copy printing.
15. Entered New Intake customer identity remains authoritative in the `ISSUED TO` name row and saved record; PDF signer/certificate identity is retained separately as evidence.
16. New Intake preserves spaces during progressive common-model-name editing and normalizes surrounding whitespace at the existing Add/Update boundary.
17. Dashboard routes printable application-window input into a fresh record search while the page is open, preserves normal focused text/ComboBox input and command shortcuts, detaches its window handler when unloaded, and defers complete-code submission until search becomes available.
18. The shared TextBox template applies horizontal padding once while preserving vertical content alignment and multiline host stretching; the Dashboard search uses `34,11,10,11` so its caret/typed text align with the icon/placeholder and remain vertically centered.
19. Dashboard pie slices use pointer hit testing to add a focus-colored hover outline and category/count/percentage tooltip while retaining the persistent labeled legend as the accessible non-hover equivalent. The chart exposes automation properties through a peer and uses weak collection notifications so navigation/refresh cannot retain discarded visuals.
20. Release publishing captures the offline verifier exit status immediately and fails before either publish mode when it is nonzero; repository validation enforces the fail-closed ordering.

## Release evidence status

The `0.9.3-alpha.1` canonical unsigned release gate completed successfully in 54.9 seconds. Repository validation and all 112 tests passed with 0 failed and 0 skipped; dependency audits reported no vulnerable packages; the offline-runtime report contained no findings; both self-contained `win-x64` publish modes, the WiX MSI, manifest, and checksum verification passed. The 70,289,687-byte EXE has SHA-256 `7fc0b209c45404b955878a490f515510c249993e31dc696737bcb380735a5f61`, and the 58,154,645-byte MSI has SHA-256 `71550a1059d586829ae32e96d44787038abe2d4579734d66cca7568c3b606872`. The manifest records `AuthenticodeSigned=false` and `UNSIGNED SYNTHETIC-DATA PILOT ONLY`; physical pointer/theme/DPI/scanner and disposable upgrade/rollback acceptance remain external target-computer work.

### Prior verified 0.9.2-alpha.1 baseline

The final `0.9.2-alpha.1` canonical unsigned release gate completed successfully in 56.7 seconds. Repository validation and all 103 tests passed with 0 failed and 0 skipped; dependency audits reported no vulnerable packages; the offline-runtime report contained no findings; both self-contained `win-x64` publish modes, the WiX MSI, manifest, and checksum verification passed. The resulting 70,288,174-byte EXE has SHA-256 `6aec0fbc1427abf90dd0d2b78961eb13f2546e25d361c3a8f37c210c63667cf5`, and the 58,142,357-byte MSI has SHA-256 `6a307652a38e198561002ab59ac33883be777c8fc557d892eaed2763b6056b6d`. The manifest records `AuthenticodeSigned=false` and `UNSIGNED SYNTHETIC-DATA PILOT ONLY`; physical scanner/WPF focus and disposable upgrade/rollback acceptance remain external target-computer work.

## External actions still required

### Code signing and application control

- Supply an organization-issued code-signing identity or approved signing service.
- Establish certificate trust and any approved timestamping process on target systems.
- Sign and verify both release artifacts; preserve release hashes and provenance.
- Obtain WDAC/AppLocker/endpoint allowlisting for the exact approved publisher or hash baseline.

### Managed deployment

- Use an authorized administrator or software-distribution system for the Program Files MSI.
- Increment one of the first three numeric MSI version fields for every release; an alpha-suffix-only change is not an MSI update.
- If using the portable EXE, approve its user-writable location and replacement procedure.
- Confirm organizational acceptance of the WiX Toolset SDK 6.0.2 license/maintenance terms used by the installer build.
- Do not run operationally from removable transfer media.
- Preserve a prior signed package and verified data backup for rollback.
- Use one designated Windows user profile unless separate per-user databases are deliberately required.

### AFNET/air-gapped authorization

- Approve the software-transfer path, malware scan, inventory, release evidence, and update/rollback procedure.
- Validate against the exact Windows baseline, Adobe build, CAC middleware/readers, scanners, endpoint controls, and storage paths.
- Approve records retention, backup media/location, restore authority, and physical paper handling.
- Complete the owning organization’s ISSM/ISSO, RMF/eMASS, data-owner, and operational acceptance work.

### Scheduler operating assumption

The schedule is hosted by the application rather than a service or privileged Windows task. Backups occur exactly at the configured times only while the application is open; otherwise the latest missed backup catches up at next launch. If unattended execution while logged off is mandatory, that is a separate deployment/service requirement and must be approved and implemented before production use.

### Test-data reset authorization

The standard-user reset control is disabled by default, phrase-confirmed, and backed up first. It is a pilot safety feature, not role-based authorization. Keep it disabled outside a formally designated test workstation.
