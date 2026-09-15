# Validation report — 0.9.4-alpha.1

## Windows release gate verified for 0.9.4 — 2026-09-15

The final canonical `.\scripts\build-release.ps1 -AllowUnsignedPilotBuild` command, invoked with the explicit Windows PowerShell executable, passed in 75.3 seconds with exit code 0. All 145 tests passed (0 failed, 0 skipped; 25-second test phase). Repository validation, application and installer dependency audits, offline-runtime review, both self-contained `win-x64` publish modes, and WiX MSI creation passed. Native-command output is retained in `artifacts/validation/release-0.9.4-final-gate.log`. The earlier successful 75.7-second PowerShell 7.6 run is retained separately; its MSI/manifest hashes are not the final package hashes below.

Release identity: application `0.9.4-alpha.1`, file/assembly `0.9.4.1` / `0.9.4.0`, MSI product version `0.9.4`, schema 6. The final manifest was created at `2026-09-15T00:49:09.7567354-06:00` and records `TestsSkipped=false`, `AuthenticodeSigned=false`, `SelfContained=true`, `OfflineRuntime=true`, `PortableSingleFile=true`, and `UNSIGNED SYNTHETIC-DATA PILOT ONLY`.

Independent rehashes match the manifest and release checksum file:

- `artifacts/release/EquipmentTrackingPlatform-0.9.4-alpha.1-win-x64-UNSIGNED-PILOT.exe` — 70,325,790 bytes; SHA-256 `08c563bf7d9554e4b49b598206d074ff7451f8c4a994bba745052a6b6b8b7c90`.
- `artifacts/release/EquipmentTrackingPlatform-0.9.4-alpha.1-win-x64-UNSIGNED-PILOT.msi` — 58,191,509 bytes; SHA-256 `013e6ab28b7314d122d88f99d85bd5e666ab07932fcd891c1f1759fa0e0dc342`.
- `artifacts/release/release-manifest.json` — 1,246 bytes; SHA-256 `80b605f2175d15a46571c0760a3c7bcc8bb50c59ffa0154b55225114139156e2`.

Read-only Windows Installer inspection reports ProductName `Equipment Tracking Platform`, ProductVersion `0.9.4`, ProductCode `{CA30E64E-E597-4B3F-9EDB-BFFF9BCC0210}`, stable UpgradeCode `{C6534286-9C99-45F3-A3AF-F70508F03208}`, Manufacturer `58 SOW`, and `ALLUSERS=1`. Both release files are `NotSigned`, as expected for the pilot path. The conventional publish checksum file verifies all 425 payload files (160,787,103 bytes); including checksums, that directory contains 426 files totaling 160,829,384 bytes. The portable directory verifies its one 70,325,790-byte EXE and contains two files totaling 70,325,890 bytes including checksums. No missing, mismatched, or unlisted payload files were found. Both dependency audit JSONs have zero vulnerable package entries; the offline report has zero findings.

New regression coverage includes exact HP recognition and negative identity cases, conservative local history/catalog lookup, scan/check selection without duplicate toggling, exact device-linked receipt commits, final-subset autoarchive, backup/restore and missing-receipt checks, unsigned-to-signed retry, recovery after commit without a prepared source, pending-workflow guards, and evidence-preserving rollback. The legacy generic status API cannot bypass signed pickup or reactivate returned devices.

Synthetic offscreen WPF layout tests load the pickup selection, signature-confirmation, and document-list screens without starting operational application services. Production-template fixture renders in `artifacts/validation/partial-pickup-qa` confirm independent selected-only marks, original-byte preservation, and return dates. Reopened canonical AcroForm fields and page widgets agree on the three device values and ReturnDate, each has a nonempty appearance stream, and interactive/signature fields remain present. These fixtures are not operational hand receipts and do not exercise a physical CAC or Adobe signing session.

The build-verification task did not install/uninstall software. The user subsequently authorized publishing the source, AI handoff files, and these verified artifacts to `LSmaroff/Equipment-Tracker` as `v0.9.4-alpha.1`. Physical scanner timing, Adobe/CAC signing, target DPI/theme acceptance, and disposable MSI upgrade/rollback still require target-workstation testing. Existing unrelated analyzer warnings remain; no tests, validation, audit, backup, or signature checks were bypassed. Unsigned artifacts remain restricted to synthetic-data testing unless the responsible organization explicitly approves different use.

## Prior verified release — 0.9.3-alpha.1

## Windows release gate verified for 0.9.3 — 2026-08-10

The canonical command `.\scripts\build-release.ps1 -AllowUnsignedPilotBuild` completed successfully in 54.9 seconds. Its 112-test phase completed in 6 seconds (112 passed, 0 failed, 0 skipped); the gate also reran repository validation, completed valid application and installer dependency inventories/vulnerability audits with no reported vulnerable packages, produced an offline-runtime report with no findings, published both self-contained `win-x64` modes, and built the WiX MSI. The publish stage now captures the offline verifier exit status immediately and refuses to start either publish mode when it is nonzero; repository validation enforces that fail-closed order.

The verified release identity is application `0.9.3-alpha.1`, file/assembly `0.9.3.1` / `0.9.3.0`, MSI product version `0.9.3`, database schema version 5, and the stable MSI UpgradeCode. The manifest records `SelfContained=true`, `OfflineRuntime=true`, `PortableSingleFile=true`, `AuthenticodeSigned=false`, `TestsSkipped=false`, release use `UNSIGNED SYNTHETIC-DATA PILOT ONLY`, and creation time `2026-08-10T22:59:13.1198613-06:00`.

Independent Windows Installer inspection reports ProductName `Equipment Tracking Platform`, ProductVersion `0.9.3`, ProductCode `{B49660A9-9AA8-49C9-B4AD-BC29B5B87B7C}`, UpgradeCode `{C6534286-9C99-45F3-A3AF-F70508F03208}`, Manufacturer `58 SOW`, and `ALLUSERS=1`. Independent Authenticode inspection reports both the EXE and MSI as `NotSigned`, matching the manifest and expected unsigned-pilot path.

Release evidence was independently rehashed and matches both `release-manifest.json` and `SHA256SUMS.txt`:

- `artifacts\release\EquipmentTrackingPlatform-0.9.3-alpha.1-win-x64-UNSIGNED-PILOT.exe` — 70,289,687 bytes; SHA-256 `7fc0b209c45404b955878a490f515510c249993e31dc696737bcb380735a5f61`.
- `artifacts\release\EquipmentTrackingPlatform-0.9.3-alpha.1-win-x64-UNSIGNED-PILOT.msi` — 58,154,645 bytes; SHA-256 `71550a1059d586829ae32e96d44787038abe2d4579734d66cca7568c3b606872`.
- `artifacts\release\release-manifest.json` — 1,246 bytes; SHA-256 `fa678706526cecfda633885ab673c6c718d1df0b944ea290d0598ab79b355830`.
- `artifacts\release\SHA256SUMS.txt` — 360 bytes; SHA-256 `7bcc9c6b7ad428d85f9e97f76e8e01213010ae623919037b1e9dadefb8c86a8d`.
- The conventional publish audit verified all 425 advertised payload hashes totaling 160,661,663 bytes. Including its `SHA256SUMS.txt`, the folder contains 426 files totaling 160,703,944 bytes.
- The portable publish EXE and `artifacts\installer\EquipmentTrackingPlatform-0.9.3-win-x64.msi` independently match the versioned release EXE/MSI sizes and hashes.

These artifacts are unsigned and restricted to synthetic-data pilot use. Physical pointer behavior, light/dark-theme and 100%/125%/150%/200% DPI layout, physical scanner/WPF focus timing, and disposable installed-product upgrade/rollback acceptance remain pending target-computer work; this verification did not install or uninstall software.

## Verified release baseline — 0.9.2-alpha.1

## Windows release gate verified for 0.9.2 — 2026-08-10

The final canonical command `.\scripts\build-release.ps1 -AllowUnsignedPilotBuild` completed successfully in 56.7 seconds after the compiled help-text wording correction. It reran repository validation and all 103 automated tests (103 passed, 0 failed, 0 skipped), completed the application and installer dependency inventories/vulnerability audits with no reported vulnerable packages, produced an offline-runtime report with no findings, published both self-contained `win-x64` modes, and built the WiX MSI.

The verified release identity is application `0.9.2-alpha.1`, file/assembly `0.9.2.1` / `0.9.2.0`, MSI product version `0.9.2`, database schema version 5, and the stable MSI UpgradeCode. The release manifest records `SelfContained=true`, `OfflineRuntime=true`, `PortableSingleFile=true`, `AuthenticodeSigned=false`, `TestsSkipped=false`, and release use `UNSIGNED SYNTHETIC-DATA PILOT ONLY`; it was created at `2026-08-10T22:18:58.0745959-06:00`.

Independent Windows Installer inspection reports ProductName `Equipment Tracking Platform`, ProductVersion `0.9.2`, ProductCode `{8D8E7703-8AF8-407A-BC15-D0C27C9F372B}`, UpgradeCode `{C6534286-9C99-45F3-A3AF-F70508F03208}`, Manufacturer `58 SOW`, and `ALLUSERS=1`. Independent Authenticode inspection reports both the EXE and MSI as `NotSigned`, matching the manifest and expected unsigned-pilot path.

Release evidence was independently rehashed and matches both `release-manifest.json` and `SHA256SUMS.txt`:

- `artifacts\release\EquipmentTrackingPlatform-0.9.2-alpha.1-win-x64-UNSIGNED-PILOT.exe` — 70,288,174 bytes; SHA-256 `6aec0fbc1427abf90dd0d2b78961eb13f2546e25d361c3a8f37c210c63667cf5`.
- `artifacts\release\EquipmentTrackingPlatform-0.9.2-alpha.1-win-x64-UNSIGNED-PILOT.msi` — 58,142,357 bytes; SHA-256 `6a307652a38e198561002ab59ac33883be777c8fc557d892eaed2763b6056b6d`.
- `artifacts\release\release-manifest.json` — 1,246 bytes; SHA-256 `6ca6b653e2be45dfab6f1e77253aa2c28b4a7b0f59b6eae1c6e4305c65aa21b6`.
- The conventional publish audit verified all 425 advertised payload hashes, totaling 160,658,591 bytes. Including the publish folder's `SHA256SUMS.txt`, the directory contains 426 files totaling 160,700,872 bytes. The installer intermediate `artifacts\installer\EquipmentTrackingPlatform-0.9.2-win-x64.msi` is 58,142,357 bytes.

These artifacts are unsigned and restricted to synthetic-data pilot use. Physical wedge-scanner/WPF focus timing and disposable installed-product upgrade/rollback acceptance remain pending target-computer work; the source/build verification did not install or uninstall software.

## Verified prior baseline — 0.9.1-alpha.1

### Source and portable checks recorded for 0.9.1

- Compiled all 117 non-generated C# application/test files during the verified Windows release gate; no compiler errors were found.
- Parsed every source XAML, project, WiX, props/config, and JSON file; all were well formed. Each declared XAML code-behind event handler was found.
- Confirmed application version `0.9.1-alpha.1`, file/assembly versions `0.9.1.1` / `0.9.1.0`, MSI product version `0.9.1`, database schema version 5, stable MSI UpgradeCode, and release-manifest schema value 5.
- Exercised migration-5 and model-catalog SQL with SQLite: legacy `Model` values backfilled into `PartNumber`, case-insensitive mapping upsert succeeded, and matching device model names updated.
- Added tests for all eight supplied field scanner samples, record-code validation/PNG generation, model-catalog migration/reuse, and separate PDF model/part output.
- Rendered a representative record ID and QR overlay against the approved 1297 template at 300 DPI. The code fits the unused footer gaps without covering form content.
- Decoded the rendered QR image back to `ETP1297:TX-20260810-143025-1A2B3C4D`.
- Recomputed the clean template SHA-256 as `00daa4ac652d592f03554e1d8e2ea9ec6083b30e9c9659330fe1dad32e599051`; the source template was not modified.
- Confirmed the updater checks MSI ProductName, stable UpgradeCode, strictly newer numeric version, SHA-256/size against the companion manifest, offline Authenticode trust, pending Recovery work, and a verified pre-update backup before starting elevated Windows Installer.
- Added an end-to-end preparation/finalization regression test using entered customer `MSgt Test T Testing` and synthetic PDF signer `MSgt Smaroff, Liam D`; the approved template's `ISSUED TO` name row and saved customer remain the entered identity while signer/certificate metadata is retained separately.
- Visually rendered the approved template and enumerated both canonical AcroForm fields and page widgets to confirm `ISSUED TO SIGNATURE` / `ISSUED TO: NAME, GRADE, ORGN` are customer fields and `ISSUED BY SIGNATURE` is the technician field. Automated tests cover the corrected defaults and legacy-settings repair.
- Confirmed the release safeguards still require application and installer dependency audits, signing, checksums, manifest output, test execution for signed field candidates, build-server shutdown and server-disabled compilation around clean WPF generated state, and the validated shortcut/icon authoring.

### Windows release gate verified for 0.9.1 - 2026-08-10

The complete release pipeline was executed on Microsoft Windows `10.0.26100` x64 with Windows PowerShell `5.1.26100.4652` and the pinned .NET SDK `10.0.110`:

```powershell
.\scripts\build-release.ps1 -AllowUnsignedPilotBuild
```

The command completed successfully with:

- restore and compilation passed;
- 89 automated tests passed, with 0 failed and 0 skipped;
- project validation passed;
- application and installer dependency inventories/vulnerability audits completed without reported vulnerable packages;
- the offline-runtime report contained no findings;
- conventional self-contained and portable single-file `win-x64` publishing passed;
- WiX 6.0.2 built the per-machine MSI successfully; and
- release-manifest sizes and SHA-256 values matched the generated EXE/MSI and `SHA256SUMS.txt`.

The `0.9.1-alpha.1` canonical gate passed after the focused and full Debug test runs. The earlier persistent-build-server correction was also verified by two consecutive `0.9.0-alpha.1` gates in the same working folder with no manual cleanup, retry, or delay; the second run recreated Debug and Release `App.g.cs` and `Views\DashboardView.g.cs` after its recorded start time. The 0.9.1 EXE, MSI, manifest, publish checksums, audit JSON, and offline report were independently rechecked.

Release output:

- `artifacts\release\EquipmentTrackingPlatform-0.9.1-alpha.1-win-x64-UNSIGNED-PILOT.exe`
- `artifacts\release\EquipmentTrackingPlatform-0.9.1-alpha.1-win-x64-UNSIGNED-PILOT.msi`
- `artifacts\release\release-manifest.json`
- `artifacts\release\SHA256SUMS.txt`

The initial run exposed missing `System.IO` imports in the updater/Authenticode services. A failed network audit then left a generated non-JSON report that the next source-validation pass tried to parse. The imports were restored, and source validation was narrowed to non-generated files while retaining strict JSON/XML checks and accurate error paths. Later reruns reproduced both a Windows Explorer handle on an otherwise emptied WiX `obj` directory and intermittent `CS2001` failures for deleted WPF `App.g.cs`/`DashboardView.g.cs` outputs. Cleanup now continues past a locked directory only after proving it empty, stops persistent .NET build servers before deleting WPF intermediates, and disables build-server reuse throughout the release chain. Any residual generated state or failed server shutdown remains a hard failure. No validation rule was suppressed.

Unsigned pilot builds are the normal personal-development path and Windows `Unknown Publisher` is expected. They remain clearly labeled by the build and manifest for synthetic-data pilot use. The optional organization-controlled signing path was not exercised and is **unverified**.

## Required gate for every release

Repeat the canonical command from the repository root and do not accept a build whose tests, validation, dependency audit, offline scan, publish, MSI build, or checksum verification fails. For an organization-signed candidate, use the existing certificate/thumbprint options and verify Authenticode separately; code signing is not required for the unsigned developer/pilot workflow.

## Required target-computer acceptance

- On a disposable target, upgrade installed `0.9.0` products to `0.9.1`; confirm the Program Files DLL is replaced, data is preserved, all older related registrations are removed so exactly one `0.9.1` registration remains, downgrades are refused, and rollback is rehearsed. This source/build verification did not install or uninstall software.
- Upgrade an installed `0.9.1` disposable target to `0.9.2`; confirm the new payload is installed, per-user data is preserved, exactly one related `0.9.2` registration remains, the older version is removed, downgrades are refused, and rollback custody is maintained.
- Scan each supplied device format with the actual wedge scanner, both with and without an Enter suffix; verify one submission, correct values, and focus on the next row.
- Type and commit a multiword common model such as `EliteBook 830 G8`; confirm each space remains while typing and the normalized value persists through SQLite, search, PDF, and Excel output.
- Create and print a synthetic 1297, then with Dashboard open and focus in separate trials on the navigation button, Refresh, a blank Dashboard surface, and a read-only result row, scan both halves of the two-copy Letter sheet without clicking search or pressing Enter. Prove exact active/archive lookup, automatic scope switching, and deferred lookup when a Dashboard refresh is still busy. Separately verify the scope ComboBox keeps native type-selection and the focused search box keeps native editing rather than either input being redirected; also verify active-device lookup from Equipment Status.
- Verify known model reuse, unknown-model prompting/skip, corrected mapping reuse, search, SQLite, Excel, and backup/restore behavior.
- Complete `FIELD-TEST-CHECKLIST.md` and obtain records, security, software-distribution, and operational-owner approval before real records are used.

## Security statement

Static validation and automated tests reduce risk but do not prove vulnerability-free behavior or constitute DISA certification, AFNET approval, RMF authorization, or an Authority to Operate.
