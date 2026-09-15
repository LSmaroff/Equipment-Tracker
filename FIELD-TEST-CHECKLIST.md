# First field-test checklist

Use synthetic/non-operational records until every blocking item is resolved.

Build-host evidence records a successful 54.9-second `0.9.3-alpha.1` canonical unsigned gate with 112 tests passed, 0 failed, and 0 skipped; validation/audits/fail-closed offline review, both publish modes, MSI creation, manifest, and checksums passed. The resulting files remain restricted to synthetic-data pilot use, and physical field, authorization, and disposable-upgrade checks remain intentionally unchecked.

Build-host evidence records a successful 56.7-second final `0.9.2-alpha.1` canonical unsigned gate with all 103 tests passing, validation/audits/offline review complete, and verified EXE/MSI checksums. The physical field, signing, authorization, and disposable-upgrade checks remain intentionally unchecked.

## Before transfer

- [ ] Build on an approved Windows workstation with the pinned .NET SDK 10.0.110 and run the full validation sequence in `VALIDATION.md`.
- [ ] Before accepting a `0.9.3-alpha.1` artifact, independently confirm and preserve its recorded 112-test canonical result, validation/audit/offline reports, manifest, checksums, EXE, and MSI; do not reuse `0.9.2` evidence.
- [ ] Independently confirm and preserve the recorded `0.9.2-alpha.1` canonical result of 103 passed, 0 failed, and 0 skipped before accepting its release artifacts.
- [ ] Use a signed release; verify both Authenticode signatures and `SHA256SUMS.txt`.
- [ ] Confirm organizational acceptance of the WiX Toolset SDK 6.0.2 license/maintenance terms used to build the MSI.
- [ ] Preserve the exact source version, build log, test result, dependency inventory, hashes, and prior rollback package.
- [ ] Transfer through approved media/process and malware-scan before and after transfer.
- [ ] Obtain application-control/EDR approval for either the MSI publisher or exact portable EXE hash.
- [ ] Confirm the target is x64 Windows, is patched, has approved Adobe/CAC middleware/scanner drivers, and uses the correct time zone and system clock.
- [ ] Confirm BitLocker, profile ACLs, endpoint protection, and physical security are active.

## Install and configure

- [ ] Prefer the MSI through an authorized administrator/software-distribution path. If using the portable EXE, place it in an approved final folder rather than running from transfer media.
- [ ] Select one designated standard-user Windows profile. Launch as that user and confirm only one app instance opens; another Windows account has a separate database/settings set.
- [ ] Run **Settings → Run readiness check** and resolve failures.
- [ ] Confirm the readiness check passes the approved template SHA-256 and exact `Pickup Signature` field.
- [ ] Configure Adobe, Completed PDF, Excel, and backup locations; save settings.
- [ ] Keep database, completed PDFs, and backups out of personal cloud-sync folders unless specifically approved.
- [ ] Put backups on a second approved physical volume/share when possible. Do not use FAT32 for potentially large full archives.
- [ ] Confirm backup storage has approved encryption, access control, retention, disposal, and removable-media/network-share handling. The ZIP and sidecar detect accidental corruption; they are not application-level encryption or a trusted digital signature.
- [ ] Confirm enough capacity for at least the configured weekly full count plus the current week’s differentials and temporary creation space.

## Prove backup and recovery

- [ ] Create **Full backup now** and confirm a `.zip` and matching `.sha256` sidecar.
- [ ] Open the ZIP only for inspection; confirm it contains a manifest, database, settings, and representative completed/original/archive PDFs.
- [ ] Confirm operators understand that logs, derived Excel, temporary print jobs, and unfinished Adobe working/recovery files are outside the scheduled archive.
- [ ] Change/add and move/delete synthetic PDFs after the full, allow a differential to run, and confirm a restore reflects the changed paths without reintroducing the old full-backup location.
- [ ] Verify the application stays open at Monday 09:00 and weekday 16:00 if exact execution is required.
- [ ] Close the app across a scheduled time, reopen it, and confirm the latest missed run catches up.
- [ ] Simulate an unavailable destination and confirm failure is visible, old backups remain, and a later retry succeeds.
- [ ] Corrupt a synthetic new Monday full before invoking retention and confirm the prior full/differential chain remains.
- [ ] On an expendable test profile/computer, restore a full and a differential. A differential must fail safely if its Monday full is absent.
- [ ] Confirm the next verified Monday full removes prior-week differentials but retains the configured number of weekly fulls.
- [ ] Verify restored database searches, PDF paths, archived records, signatures, and file ACLs.

## Exercise the complete workflow

- [ ] Test intake, scan/manual entry, correction, duplicate serial handling, CAC refresh/removal, Adobe save/close, and finalization.
- [ ] In a New Intake row, progressively type a multiword common model such as `EliteBook 830 G8`; confirm each space remains visible while typing, then Add and Update the row and verify surrounding whitespace is normalized without removing internal spaces.
- [ ] Enter a synthetic customer name/rank, use a different synthetic signer, and confirm the `ISSUED TO` name row, Dashboard, export, and PDF filename retain the entered customer while signer/certificate metadata records the signer separately.
- [ ] In the approved Adobe/CAC workflow, confirm each synthetic signature shows valid trust/revocation status. The application detects signature presence and signer metadata but is not a substitute for certificate-chain validation.
- [ ] Force-close the app during intake and closeout; confirm Recovery can resume safely.
- [ ] Test status changes, pickup, closeout, organization archive placement, search, and Excel export.
- [ ] Open Dashboard, leave focus on the Dashboard navigation button, Refresh, a blank Dashboard surface, and a read-only result row in separate trials, then scan active and archived printed 1297 codes without clicking search or pressing Enter. Confirm a fresh exact search, automatic active/archive scope selection, no lost/duplicated characters after navigating away and back, and successful deferred search when a refresh is still running. Separately confirm the scope ComboBox keeps native type-selection and the focused search box keeps native editing rather than either input being redirected.
- [ ] In light and dark themes at 100%, 125%, 150%, and 200% display scaling, focus the Dashboard search and representative single- and multiline TextBoxes across intake, status, settings, dialogs, and Recovery. Confirm each caret and first typed glyph use the intended content/placeholder inset, Dashboard padding `34,11,10,11` is vertically centered, multiline hosts stretch correctly, and icons, borders, selection, validation, wrapping, and scrolling remain correct.
- [ ] Group the Dashboard pie chart by Status, Model, and Customer; hover every representative large and small slice and verify a visible focus-colored outline plus a tooltip with the exact category, device count, and percentage. Move outside and leave the chart to confirm both clear. Repeat for a single-slice chart, no-data state, grouping changes, refresh, and data replacement; confirm no clipping, jitter, stale hover state, geometry change, or loss of the persistent labeled legend.
- [ ] Print synthetic active and archived 1297s; verify two readable copies, signatures, cut guide, paper settings, and temporary-file cleanup.
- [ ] Test low-space, locked-PDF, disconnected backup drive, unavailable Adobe, missing reader, and malformed scanner input behavior.
- [ ] Generate a support package and verify it excludes operational PDFs/database and does not reveal sensitive paths unnecessarily.
- [ ] Confirm runtime operation with outbound networking blocked.

## Update and rollback rehearsal

- [ ] Install/copy a newer synthetic build using `DEPLOYMENT.md`; confirm data and settings survive.
- [ ] Confirm the MSI replaces the older installed payload and blocks a downgrade.
- [ ] For the `0.9.0` to `0.9.1` correction, confirm the Program Files application DLL is replaced and only one related `0.9.1` MSI registration remains after the authorized upgrade.
- [ ] For the `0.9.1` to `0.9.2` UX correction, confirm the Program Files application DLL is replaced, per-user data is preserved, and only one related `0.9.2` MSI registration remains after the authorized upgrade.
- [ ] For the `0.9.2` to `0.9.3` presentation correction, confirm the Program Files application DLL is replaced, per-user data is preserved, exactly one related `0.9.3` MSI registration remains, downgrade is blocked, and the prior approved package plus compatible verified backup remain available for rollback.
- [ ] Confirm every newer MSI changes one of the first three numeric version fields; changing only an alpha suffix is not an upgrade.
- [ ] From 0.9 or later, test Settings → Install update package with a valid newer signed MSI and companion manifest; confirm exact identity/hash/signature checks, pre-update backup, UAC, clean shutdown, and preserved data.
- [ ] Confirm the update command refuses a same/older version, wrong UpgradeCode, mismatched manifest/hash, and a system with unfinished Recovery work; document the approved response to an unsigned-package warning.
- [ ] Rehearse rollback using the prior approved package and a compatible verified backup.

## Stop conditions

Do not move to operational records if any of these remain: unsigned/unapproved binaries, failed readiness checks, unverified restore, backups only on the protected drive when drive-loss protection is required, a requirement for logged-off exact-time backups without a separately approved unattended design, incorrect system time, template mismatch, missing application-control approval, CAC/Adobe incompatibility, unresolved recovery items, or missing records/security authorization.
