# AFNET / NIPR Deployment Review Checklist

This checklist is a preparation aid for the owning organization. It is not an approval, STIG checklist, Authority to Operate, or substitute for the responsible ISSM, ISSO, SCA, Authorizing Official, records manager, privacy office, or software-distribution authority.

## 1. Ownership and system boundary

- [ ] Identify the application owner, technical owner, data owner, and support contact.
- [ ] Identify the authorizing boundary in which the application will run.
- [ ] Determine whether names, duty phone numbers, ticket data, equipment serials, and signed 1297s are CUI, PII, records, or otherwise controlled in the intended mission context.
- [ ] Document every dependency in the assessed boundary: Windows, .NET, Adobe, CAC middleware, scanner firmware, PDF template, local storage, backup destination, and any approved network share.
- [ ] Obtain local ISSM/ISSO/SCA and software approval before operational deployment.

## 2. Source and build provenance

- [ ] Store source in an approved, access-controlled repository with change history and peer review.
- [ ] Build on an approved workstation or software factory.
- [ ] Install and record the pinned .NET SDK 10.0.110 servicing baseline used by `global.json`.
- [ ] Restore packages only from an approved internal NuGet source or mirror.
- [ ] Preserve the application and installer `artifacts\security\*-dependency-inventory.json` and `*-package-audit.json` files, build logs, test results, source commit, release version, and checksums as release evidence.
- [ ] Run `scripts\setup.ps1` without `-SkipTests`.
- [ ] Run locally approved SAST, SCA, secret scanning, and malware scanning.
- [ ] Review all NuGet audit results; do not suppress an advisory without documented risk acceptance.
- [ ] Confirm organizational acceptance of the WiX Toolset SDK 6.0.2 license/maintenance terms and document the reviewed installer dependency.
- [ ] Code-sign approved release binaries; unsigned `UNSIGNED-PILOT` output is restricted to explicitly approved synthetic-data testing.
- [ ] Validate the published files against `SHA256SUMS.txt` before distribution.
- [ ] Build `artifacts\release` with `scripts\build-release.ps1`; preserve both the release manifest and exact source version.
- [ ] Verify Authenticode on the application-owned installed payload, portable EXE, and final MSI.

## 3. STIG and configuration review

- [ ] Review the current Application Security and Development STIG/SRG in STIG Viewer.
- [ ] Review applicable Windows endpoint, Adobe Acrobat/Reader, antivirus, application-control, and account-management STIGs.
- [ ] Map findings and not-applicable determinations to the actual deployment, not only the source code.
- [ ] Confirm the application runs as the signed-in user and is not granted local-administrator rights.
- [ ] Designate the Windows user profile that owns the operational database/settings; document whether separate accounts are prohibited or intentionally isolated.
- [ ] Confirm WDAC/AppLocker or the local application-control mechanism permits only the approved signed/hash baseline.
- [ ] Confirm endpoint patching keeps Windows, .NET, Adobe, CAC middleware, and scanner drivers within the approved patch baseline.

## 4. Data protection and records handling

- [ ] Confirm BitLocker or the approved endpoint encryption control is enabled.
- [ ] Confirm `%LOCALAPPDATA%\58SOW\EquipmentTrackingPlatform`, its temporary `PrintJobs` folder, and output folders inherit approved NTFS ACLs.
- [ ] Approve the completed-PDF, archive, Excel, log, and backup locations.
- [ ] Place scheduled backups on an approved second physical volume when drive-loss protection is required; document any exception.
- [ ] Confirm the destination is not FAT32 and has room for retained weekly fulls, current-week differentials, and temporary creation space.
- [ ] Confirm the backup folder neither contains nor is contained by application-data/completed-PDF folders.
- [ ] Define retention and disposal for active PDFs, archived PDFs, SQLite, Excel, logs, and backups.
- [ ] Protect backup archives with approved storage encryption, ACLs, media custody, and restore authority; SHA-256 sidecars are integrity aids, not encryption or trusted digital signatures.
- [ ] Prohibit storage on personal cloud-sync locations or removable media unless specifically approved.
- [ ] Do not place the SQLite database on a shared drive for concurrent use.
- [ ] Test backup restoration and verify that restored permissions and markings remain correct.
- [ ] Test a Monday full, each weekday differential, missed-run catch-up, 15-minute failure retry, and unavailable-destination recovery.
- [ ] Confirm prior-week differentials remain when a new full fails or is corrupted and are removed only after retention independently re-verifies a new full.
- [ ] Restore a differential with its Monday full present, then confirm safe refusal when the base full is absent.
- [ ] Move/delete a synthetic PDF after the full and confirm the differential restore does not reintroduce the old full-backup path.
- [ ] Confirm backup creation refuses a missing/outside/non-PDF official database path and a protected record reached through a child reparse point.

## 5. Operational and security testing

- [ ] Test with the exact approved 1297 template and all configured field names.
- [ ] Test the initial customer signature, exact Pickup Signature, application-filled issue/return dates and quantity, and Adobe save/close behavior; confirm the approved template remains free of PDF JavaScript.
- [ ] Test one CAC, two CACs in separate readers, card removal, expired certificates, and a reader with no card.
- [ ] Confirm removed-card certificates disappear after refresh and cached `CurrentUser\My` certificates are not shown.
- [ ] Test every scanner model and representative Data Matrix/barcode format used by the organization.
- [ ] Test malformed, extremely long, duplicate, and mixed-case serial inputs.
- [ ] Progressively type a multiword common model name with spaces in New Intake, add/update the row, and confirm the normalized full name is retained in the database, PDF, Excel export, and searches.
- [ ] With Dashboard open and focus in separate trials on navigation, Refresh, a blank Dashboard surface, and a read-only result row, scan active and archived printed 1297 codes without first clicking search or pressing Enter. Confirm exact lookup, scope switching, first-character retention, and completion after a concurrent Dashboard refresh. Separately confirm the scope ComboBox keeps native type-selection and the focused search box keeps native editing rather than either input being redirected.
- [ ] Test archive creation for every organization name, including punctuation and Windows-reserved names.
- [ ] Verify the archive PDF hash/copy behavior, database update, audit record, and deletion of the old active copy.
- [ ] Verify a failed copy or database operation leaves the active record recoverable.
- [ ] Test search, status changes, closeout, Excel export, restart recovery, and power-loss recovery with synthetic data.
- [ ] From both Active 1297s and Archive, test **Print 1297's** with synthetic filled and signed forms. Confirm one portrait Letter sheet contains two readable copies; every form text value uses uniform bold typography, correct alignment, padding, and wrapping inside its original box; both signatures are visible; the cut guide is centered; and the tracked PDF hash/path does not change.
- [ ] Keep the printing notice open through the print operation, then close it and confirm the temporary print sheet is deleted. Also confirm viewer-lock retry and next-startup cleanup, support-package exclusion, and approved physical paper handling/disposal procedures.
- [ ] Test the signed MSI major upgrade and the portable EXE replacement procedure; confirm operational data survives both.
- [ ] Confirm each newer MSI changes one of the first three numeric product-version fields; alpha-suffix-only changes are not accepted as updates.
- [ ] Test signature/hash rejection, rollback-package custody, and downgrade blocking.

## 6. Known design limitations requiring acceptance or redesign

- [ ] Single-workstation architecture; no in-application role-based access control.
- [ ] SQLite is not application-layer encrypted.
- [ ] Certificate trust chain, revocation, and trusted timestamp validation are intentionally not performed.
- [ ] Smart-card certificate enumeration uses Windows reader-bound provider APIs for compatibility and must be validated with the approved CAC middleware and reader fleet.
- [ ] Adobe signature behavior remains an external dependency; the approved template contains no PDF JavaScript.
- [ ] No automatic/network update channel; the local-MSI Settings command and all packages remain under the authorized software-distribution, signing, UAC, application-control, and provenance process.
- [ ] Scheduled backups are hosted in the application and do not run while it is closed/logged off; accept this constraint or approve a separate unattended-execution design.
- [ ] The portable EXE extracts required native/content payload under the user temporary area; validate endpoint controls or use the MSI.

## 7. Release decision

- [ ] Security findings are resolved, mitigated, or formally accepted.
- [ ] Functional acceptance is signed by the operational owner.
- [ ] Required RMF/eMASS artifacts and STIG evidence are complete.
- [ ] Release binaries, hashes, source version, and rollback package are preserved.
- [ ] Deployment, rollback, incident-response, and support procedures are approved.

## 0.9.3-alpha.1 pilot checks

Recorded build-host evidence: the canonical unsigned gate passed in 54.9 seconds with 112 passed, 0 failed, and 0 skipped; validation, dependency audits, fail-closed offline review, both publish modes, MSI creation, manifest, and checksums passed. The resulting EXE/MSI are `NotSigned` and restricted to synthetic-data pilot use. This does not complete the unchecked target-environment, authorization, physical pointer/theme/DPI/scanner, or upgrade items below.

- [ ] Confirm the application reports `0.9.3-alpha.1`, the MSI reports `0.9.3`, schema remains 5, and the stable UpgradeCode is unchanged.
- [ ] Independently preserve and review the recorded 112-test result, validation/audit/offline reports, both publish outputs, release manifest, checksums, EXE, and MSI before accepting a `0.9.3` artifact; do not reuse `0.9.2` evidence as proof of this build.
- [ ] In both light and dark themes at 100%, 125%, 150%, and 200% display scaling, inspect the Dashboard search and representative single- and multiline TextBoxes throughout intake, status, settings, dialogs, and Recovery. Confirm the caret and first typed glyph align with the intended placeholder/content inset, Dashboard padding `34,11,10,11` remains vertically centered, multiline hosts stretch correctly, and no icon, border, selection, validation, wrapping, or scrolling behavior regresses.
- [ ] Group the Dashboard chart by Status, Model, and Customer and hover every representative large and small slice. Confirm only the slice under the pointer gains a visible focus-colored outline; its tooltip shows the exact category, device count, and percentage; the outline and tooltip clear outside the pie and on pointer leave; and no slice clips, jitters, or changes size.
- [ ] Repeat pie hover with a single-slice chart, no-data state, after changing grouping, during/after refresh, and after data replacement. Confirm no stale highlight/tooltip remains and the persistent labeled legend continues to provide the same values without hover or reliance on color alone.
- [ ] Upgrade an authorized disposable `0.9.2` installation to `0.9.3`; confirm Program Files contains the new payload, per-user data survives, exactly one related `0.9.3` registration remains, the stable UpgradeCode relates the products, downgrade is blocked, and rollback custody is maintained.

## 0.9.2-alpha.1 pilot checks

Recorded build-host evidence: the final canonical unsigned gate passed in 56.7 seconds with 103 passed, 0 failed, and 0 skipped; validation, dependency audits, offline review, both publish modes, MSI creation, manifest, and checksums passed. This does not complete the unchecked target-environment, signing, authorization, physical scanner/focus, or upgrade items below.

- [ ] Independently preserve and review the canonical `0.9.2-alpha.1` test count, validation/audit/offline reports, release manifest, checksums, EXE, and MSI; do not reuse the `0.9.1` evidence as proof of this build.
- [ ] Confirm the application reports `0.9.2-alpha.1`, the MSI reports `0.9.2`, schema remains 5, and the stable UpgradeCode is unchanged.
- [ ] Verify progressive typing of `EliteBook 830 G8` retains every space, then confirm Add and Update normalize only surrounding whitespace.
- [ ] While Dashboard is open, place focus on non-text, non-choice controls outside its search box or on a blank Dashboard surface and scan a synthetic active and archived `ETP1297:` code. Confirm the scan starts a fresh query, moves to search, submits without click/Enter, switches scope correctly, and does not duplicate characters or handlers after navigating away and back. Confirm the focused search box and other text-entry controls retain native editing and ComboBox controls retain native type-selection.
- [ ] Begin a Dashboard refresh and scan a complete synthetic record code before refresh finishes; confirm the pending lookup runs when search becomes available.
- [ ] Upgrade an authorized disposable `0.9.1` installation to `0.9.2`; confirm Program Files contains the new payload, user data survives, exactly one related `0.9.2` registration remains, and downgrade is blocked.

## 0.9.1-alpha.1 pilot checks

- [ ] Run the startup readiness check as the intended standard user.
- [ ] Confirm the user can write only to approved application-data and output locations.
- [ ] Verify the included cleaned template hash and exact `Pickup Signature` field.
- [ ] Confirm operation with outbound network access blocked.
- [ ] Exercise Recovery after forced application termination during intake and closeout.
- [ ] Enter a synthetic customer, sign with a deliberately different synthetic identity, and confirm the `ISSUED TO` name row and Dashboard retain the entered customer while signer metadata remains separate.
- [ ] Create, tamper-test, stage, and apply a backup restore.
- [ ] Create a full plus differential chain containing synthetic completed, original-signed, and archived PDFs; verify the restored chain.
- [ ] Confirm the application is open at 09:00/16:00 for exact runs and that next-launch catch-up is acceptable.
- [ ] Confirm completed/archive PDFs are preserved during retention and test-data reset.
- [ ] Generate and inspect a sanitized support package.
- [ ] Sign release binaries using the approved organizational process.
- [ ] Install/update with the MSI through an authorized admin/deployment system, or document the approved portable location and replacement control.
- [ ] Keep **Allow test data reset** disabled on production workstations unless formally authorized.
