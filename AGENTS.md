# Equipment Tracking Platform repository instructions

## Authority and scope

- The files currently in this working folder are the source of truth. Do not replace them with ZIP archives, generated files, or remembered code from another task.
- Preserve unrelated user changes and every existing feature. Never remove or materially change a feature without explicit approval.
- Keep fixes narrowly scoped. Reproduce reported failures before fixing them when practical.
- This is an offline-first Windows desktop application. Do not introduce a mandatory cloud service, external API, telemetry, listener, or runtime Internet dependency.

## Repository layout

- `EquipmentTrackingPlatform.sln`: application and automated-test solution.
- `src/EquipmentTracking.App`: .NET WPF application, models, views/view models, services, assets, configuration, and the approved PDF template.
- `tests/EquipmentTracking.Tests`: xUnit unit and integration tests.
- `installer/EquipmentTracking.Installer`: WiX MSI project and authoring.
- `scripts`: restore, build, validation, security, offline-review, publish, signing, and release automation.
- `artifacts`: generated publish, installer, security, validation, and release evidence. Do not treat generated output as source.
- `docs/PROJECT_STATUS.md`: living architecture, verification, limitations, and handoff record.

## Supported baseline

- Windows x64 only; the verified host reports Microsoft Windows `10.0.26100` x64.
- .NET SDK `10.0.110`, pinned by `global.json` with `latestPatch` roll-forward in the 10.0.1xx band.
- C#/.NET 10 WPF `WinExe`, target `net10.0-windows`, `UseWPF=true`, self-contained `win-x64` release output.
- SQLite through `Microsoft.Data.Sqlite.Core 10.0.10` and `SQLitePCLRaw.bundle_winsqlite3 2.1.11`, using the Windows-serviced `winsqlite3.dll`.
- WiX Toolset SDK `6.0.2`; x64, per-machine MSI under Program Files. Installation and MSI update may elevate, but normal application operation remains `asInvoker` for a standard user.
- Verified release baseline: application `0.9.4-alpha.1`, file/assembly `0.9.4.1` / `0.9.4.0`, MSI product version `0.9.4`, and SQLite schema version `6`. Standalone validation and the canonical unsigned release gate pass; the suite reports 145 tests with 0 failed and 0 skipped. The manifest classifies the generated files as `UNSIGNED SYNTHETIC-DATA PILOT ONLY`. Signed partial-pickup receipts are child documents under one parent 1297; return statuses commit only after new-signature verification, with automatic archive on the final subset.
- Prior verified baseline: application `0.9.3-alpha.1`, file/assembly `0.9.3.1` / `0.9.3.0`, MSI product version `0.9.3`, and SQLite schema version `5`. Its preserved canonical gate evidence records 112 tests passed with 0 failed and 0 skipped.
- Prior verified baseline: application `0.9.2-alpha.1`, file/assembly `0.9.2.1` / `0.9.2.0`, MSI product version `0.9.2`, and SQLite schema version `5`. Its preserved canonical gate evidence records 103 tests passed with 0 failed and 0 skipped.
- Release publishing is fail-closed on the offline-runtime review: a nonzero verifier exit stops the process before either publish mode, and repository validation enforces that ordering.

## Required commands

Run from the repository root in Windows PowerShell.

~~~powershell
# Restore, dependency audit, Debug build, and tests
.\scripts\build.ps1 -Configuration Debug

# Repository structure and semantic validation
.\scripts\validate.ps1

# Standalone dependency and offline-runtime checks
.\scripts\security-scan.ps1
.\scripts\verify-offline.ps1 -FailOnFinding

# Canonical complete unsigned developer/pilot release gate
.\scripts\build-release.ps1 -AllowUnsignedPilotBuild
~~~

NuGet restore and vulnerability auditing require access to the configured feed even though the published application operates offline. On a managed network, use the scripts' `-NuGetSource` option with an approved mirror.

Unsigned pilot output and the Windows `Unknown Publisher` prompt are expected for this personally maintained development path. Do not make code signing mandatory. Keep `UNSIGNED-PILOT` artifacts restricted to synthetic-data testing unless the responsible organization explicitly approves a different use.

## Definition of a completed change

- The reported failure was reproduced when practical and the root cause, not merely the symptom, was corrected.
- Behavioral changes include relevant source and automated tests; shared changes also update validation, release notes, installer authoring, and documentation as applicable.
- Relevant tests run after every behavioral change.
- Security, backup, restore, migration, signed-PDF preservation, offline-runtime, and installer checks are not weakened or bypassed to obtain a pass.
- Release work is complete only after the canonical release command passes tests, validation, security/offline checks, both publish modes, and MSI creation, with expected files under `artifacts/release`.
- If several errors appear, continue through the complete repair and release cycle rather than stopping after the first fix.

## Safety and authority boundaries

- Use only synthetic records, PDFs, databases, scanner values, and backups during development and validation. Never modify real operational data.
- Do not commit, push, publish a GitHub release, delete files, alter deployment systems, or distribute artifacts without explicit permission.
- Do not commit PFX files, certificates, passwords, private keys, signing tokens, operational PDFs, or operational databases.
- Preserve original signed intake PDFs, working/status PDFs, finalized closeout PDFs, audit evidence, migration guarantees, and backup/restore safeguards.
- The local SQLite database is a single-user, single-workstation store. Do not place it on a shared drive for concurrent use.
- CAC signatures provide evidence of signature and signer identity metadata; certificate-chain, revocation, and trusted-timestamp validation are intentionally outside the application.
- The installed app must remain usable by a non-administrator. Elevation is limited to legitimate machine-wide installation/update operations.
