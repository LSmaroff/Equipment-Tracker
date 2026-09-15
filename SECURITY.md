# Security Policy and Deployment Notes

## Status

Equipment Tracking Platform 0.9.3-alpha.1 is a verified offline unsigned field-test pilot build. Its canonical gate passed with all 112 tests, repository validation, dependency audits, offline-runtime review, both publish modes, MSI creation, and checksum verification complete. The manifest records `AuthenticodeSigned=false` and restricts release use to `UNSIGNED SYNTHETIC-DATA PILOT ONLY`. This repository includes security hardening and assessment aids, but it is **not a DISA-certified product, an Authority to Operate, or evidence that a particular AFNET/NIPR deployment is compliant**. The owning organization must complete its normal RMF, ISSM/ISSO, SCA, software approval, vulnerability management, and deployment review.

## Data handled

The application can process names, rank/grade, duty phone numbers, organization, ticket numbers, equipment serial numbers, PDF signatures, and operational status. Treat the database, PDFs, Excel report, logs, and backups according to the data owner’s marking and handling rules. The application does not add classification or CUI markings.

The signed PDF is the authoritative electronic evidence of signature. The application detects signature presence and signer metadata but does not replace Adobe/organizational certificate-chain, trust, or revocation validation. A two-copy print sheet is a temporary visual derivative for paper handling and is not substituted for the tracked signed PDF. New records do not duplicate certificate subject or thumbprint values into the database or Excel report. The application never reads, stores, exports, or logs a CAC PIN or private key.

## Implemented controls

- Runs as the signed-in user (`asInvoker`) and does not request elevation.
- No network listener, server component, telemetry, automatic network updater, or runtime Internet dependency. The attended local-MSI command remains subject to authorization and UAC.
- Scheduled full/differential archives protect SQLite, settings, and official PDFs; payload hashes and SQLite integrity are checked before acceptance and restore. Differentials represent new, changed, moved, and deleted PDFs relative to the weekly full.
- Scheduled creation cross-checks official database paths against the completed-record root and rejects missing/outside/non-PDF/reparse-point records rather than accepting an incomplete chain.
- Prior-week differentials are deleted only after retention independently re-verifies the next weekly full. Unreadable or corrupt archives are preserved for review rather than silently deleted.
- Restore is staged, schema-bounded, hash-checked again at startup, path-traversal guarded, and preceded by a local safety copy. PDF application is non-destructive.
- Parameterized SQLite statements and an allow-listed status catalog.
- Length limits for user and scanner input, a 100 MB signature-reader PDF limit, and regex timeouts for PDF signature discovery.
- Active-serial uniqueness enforced in both application logic and SQLite.
- Transactional database updates with audit records.
- Verified SHA-256 copy before final PDF placement and organization archiving.
- File and directory names are normalized, length-limited, and protected from Windows reserved names.
- Smart-card enumeration is limited to readers that currently report a card present. Cached certificates in the user certificate store are not enumerated.
- Only one best identity/signing certificate is shown per inserted reader/card.
- NuGet audits include direct and transitive packages; known vulnerability warnings NU1901 through NU1904 are build errors.
- The bundled vulnerable SQLite native package was removed. The application uses the Windows-serviced `winsqlite3.dll` through `SQLitePCLRaw.bundle_winsqlite3`.
- Logs remove control characters, exclude exception stack traces and certificate contents, rotate at 10 MB, and retain 30 days.
- Working files remain under the current user profile and are not written to system locations.
- Two-copy print sheets remain in the current user's local application-data folder, are excluded from support packages, and are deleted when the operator closes the printing notice. Locked files receive bounded background retry; abandoned derivatives are removed at the next startup and by the pilot test-data reset.
- Print-sheet generation compares the selected source PDF's SHA-256 before and after composition and refuses the result if the source changed concurrently.
- The print-only PDF redraws canonical text values in a uniform embedded font and copies signature appearances exactly; the database, active/archive path, canonical field values, and preserved signed original are not modified.
- Release automation produces a conventional machine-wide MSI and a portable single EXE, SHA-256 manifests, and signing hooks. The Settings update command accepts only a strictly newer MSI with the expected product/UpgradeCode, checks offline Authenticode trust and a companion manifest when present, blocks unfinished Recovery work, and creates a pre-update backup. Updates remain an authorized offline distribution action.

## Important deployment dependencies

The application database is not independently encrypted by the application. Deploy only to an approved Windows endpoint where operating-system controls such as BitLocker, profile ACLs, endpoint protection, patching, application control, and approved backup handling are enforced.

The default completed-PDF, Excel, and scheduled-backup paths are under the user’s Documents folder. A same-drive backup does not protect against physical-drive failure. Backup ZIP hashes detect accidental corruption, but the application does not independently encrypt or digitally sign backup archives. The local authorizing official or data owner must approve encryption, ACLs, retention/disposal, any second volume, network share, synchronization service, removable media, or cross-domain movement. Avoid FAT32 because a full archive can exceed its 4 GB single-file limit.

Application state is per Windows user. Use one designated operator profile unless separate per-user databases are deliberately required and governed.

Use the pinned .NET SDK 10.0.110 servicing baseline and an approved internal NuGet mirror for builds on managed networks. Do not restore packages directly from the public Internet on AFNET unless explicitly authorized. The published application has no NuGet dependency at runtime.

The MSI build pins WiX Toolset SDK 6.0.2. Confirm the organization accepts its applicable Open Source Maintenance Fee terms before use, and repeat security/support/license review before changing the WiX major version.

## Build verification

Run from the repository root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\setup.ps1
```

The setup validates repository structure, restores packages, audits direct and transitive dependencies, builds, and runs tests. A separate audit can be run with:

```powershell
.\scripts\security-scan.ps1
```

Before production deployment, also perform:

1. Review against the current Application Security and Development STIG and applicable Windows/.NET/Adobe STIGs.
2. Static analysis and local approved SAST/SCA scans.
3. Malware scan and software-signing review of the published binaries.
4. Functional testing with the approved 1297 template, CAC middleware, Adobe build, scanner models, and organizational paths.
5. Verification of endpoint patch status and the installed Windows SQLite component.
6. Validation of the reader-bound smart-card provider path with the approved CAC middleware and reader models.
7. Full/differential creation, missed-run catch-up, destination-loss, restoration, retention, incident-response, and account-removal tests.
8. MSI/portable update and rollback tests, Authenticode verification, and WDAC/AppLocker/EDR approval.

Use `AFNET-DEPLOYMENT-CHECKLIST.md` as a release-preparation aid.

## Reporting a vulnerability

Do not place operational data, CAC information, or real 1297s in a public issue. Report security findings through the organization’s approved internal process and include the application version, affected file/method, reproduction steps using synthetic data, and relevant logs with sensitive values removed.

## Engineering references

- DISA STIG/SRG library: `https://public.cyber.mil/stigs/`
- DoD Enterprise DevSecOps guidance: `https://dodcio.defense.gov/library/`
- NuGet package vulnerability auditing: `https://learn.microsoft.com/nuget/concepts/auditing-packages`
- Windows smart-card architecture and card-local certificate stores: `https://learn.microsoft.com/windows/security/identity-protection/smart-cards/smart-card-architecture`

## 0.9.3-alpha.1 release-readiness delta

- Applying TextBox padding once and adding pie-slice hover emphasis are local presentation changes. They add no persisted data, logging, telemetry, listener, external API, or runtime network dependency.
- The hover tooltip displays only the category, device count, and percentage already represented by the chart and labeled legend. It does not expose a new data category; the persistent legend remains the non-hover and non-color-only equivalent.
- The chart exposes its existing automation name/help text through a peer and subscribes to collection changes through the WPF weak-event manager, preventing discarded chart visuals from being retained by later navigation or refresh activity.
- Release publishing captures a nonzero offline-verifier exit immediately and stops before either output mode; repository validation enforces this fail-closed security boundary.
- The canonical unsigned gate passed in 54.9 seconds with 112 tests passed, 0 failed, and 0 skipped. Application and installer audits reported no vulnerable packages, the offline-runtime report contained no findings, both publish modes and MSI creation passed, and independently recomputed hashes match the manifest and checksum file. Physical pointer/theme/DPI/scanner behavior and disposable installed-product upgrades remain target-environment acceptance work.

## 0.9.2-alpha.1 verified release delta

- The Dashboard type-to-search change routes printable keyboard/scanner text only within the local application window while Dashboard is loaded. It does not add logging, telemetry, a listener, a network dependency, or a new persisted data category.
- Focused text-entry and ComboBox controls retain their normal input, command-modified shortcuts are not redirected, and the Dashboard removes its window-level handler when unloaded.
- Common-model editing now retains in-progress spaces, but existing length limits and commit/database normalization remain in force.
- The final canonical unsigned release gate passed in 56.7 seconds with 103 tests passed, 0 failed, and 0 skipped. Application and installer audits reported no vulnerable packages, the offline-runtime report contained no findings, both self-contained publish modes passed, and the EXE/MSI hashes match the manifest and `SHA256SUMS.txt`. Physical scanner/focus behavior and disposable installed-product upgrades remain target-environment acceptance work.

## 0.9.1-alpha.1 verified baseline controls

- Runtime networking, telemetry, automatic network updates, and external API use are intentionally absent.
- `scripts\verify-offline.ps1` scans source and publish inputs for prohibited runtime-network patterns.
- Support packages exclude the operational database and PDFs by default and sanitize profile, machine, and path information.
- Database restores are schema-bounded, hash-verified, integrity-checked, staged, and reverified before replacement.
- Scheduled full backups contain the database, settings, and completed/original/archive PDFs. Differentials use a complete database snapshot plus PDFs added/changed since Monday and markers for moved/deleted paths.
- The application-hosted scheduler catches up the latest missed run but does not execute while the application is closed or no user session is active.
- The MSI installs under Program Files and requires an authorized administrator or deployment system; the portable EXE is the no-install alternative.
- Workflow journals allow interrupted intake/closeout operations to resume without duplicating committed records.
- Important PDFs are copied with SHA-256 verification and recorded as file artifacts.
- Retention maintenance does not delete official archived 1297 PDFs.
- The non-administrator test-data reset is disabled by default and is not a substitute for role-based authorization.
- Production signing requires an organization-controlled private key; secrets and certificates must not be committed to GitHub.
