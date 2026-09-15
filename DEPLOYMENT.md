# Deployment and offline updates

## Choose one release path

### Managed installation — recommended

Use the versioned `.msi` through an authorized administrator or software-distribution system. The MSI installs a conventional self-contained x64 payload below:

```text
%ProgramFiles%\58 SOW\Equipment Tracking Platform
```

It creates Start-menu and desktop shortcuts. A newer MSI uses the stable UpgradeCode to replace the older application payload, rolls the removal back if the upgrade fails, and blocks downgrades. Windows Installer compares only the first three numeric product-version fields, so every MSI release must change one of them. For example, use `0.9.3-alpha.1` after `0.9.2-alpha.1`; changing only the alpha suffix is not a valid MSI upgrade.

Do not deploy a rebuilt MSI under the same three-part product version. Depending on ProductCode and invocation, Windows Installer can enter maintenance mode or register another related product without replacing the older application DLL. The model-entry and Dashboard-search release is therefore `0.9.2`, not another `0.9.1` build. The earlier customer-identity correction remains recorded as the `0.9.1` release.

The shared TextBox-alignment and pie-hover release is versioned `0.9.3-alpha.1` / MSI `0.9.3` so its MSI can replace `0.9.2`. Its canonical gate passed in 54.9 seconds with 112 tests, clean validation/audits, fail-closed offline review before publication, both publish modes, MSI creation, and matching manifest/checksum evidence. The generated files are explicitly unsigned synthetic-data pilot artifacts; do not deploy them operationally or treat them as organization-approved signed field packages.

The `0.9.2-alpha.1` canonical gate passes with 103 tests, clean validation/audits/offline review, both publish modes, and MSI creation. Its manifest and independently recomputed hashes agree. The generated files are explicitly unsigned synthetic-data pilot artifacts; do not deploy them operationally or treat them as organization-approved signed field packages.

For an attended offline update, open Settings and select **Install update package…**. Keep the versioned MSI and its `release-manifest.json` together so the app can verify the listed SHA-256 and size. The app refuses packages for a different ProductName/UpgradeCode and same or older versions, blocks the update while Recovery has unfinished work, creates a verified local database/settings backup, starts Windows Installer, and closes. Administrator approval is still required; the in-app command does not bypass endpoint policy or application control.

An installed version older than 0.9 does not contain this command. Use the normal authorized MSI installation path once to reach 0.9; the in-app path applies to later strictly newer MSI releases.

### Portable field-test execution

Use the versioned `.exe` when installation is unavailable. Copy it to an approved, access-controlled user-writable application folder on the target computer. Do not run it directly from email, a browser download location, a shared folder, or removable transfer media.

The portable executable bundles managed assemblies and extracts required native/content files under the signed-in user’s temporary area at runtime. Confirm endpoint application control and security tooling permit this behavior. The MSI avoids that runtime extraction pattern and is preferred for managed deployment.

## Build a release

From an approved Windows build machine with the pinned .NET SDK 10.0.110, the approved NuGet source, and an organization-approved signing identity:

```powershell
.\scripts\build-release.ps1 `
  -CertificateThumbprint '<approved-thumbprint>' `
  -TimestampUrl '<approved-timestamp-url>'
```

The output is under `artifacts\release` and contains the portable EXE, MSI, `release-manifest.json`, and `SHA256SUMS.txt`. The script refuses unsigned output by default. For an explicitly approved synthetic-data lab only, `-AllowUnsignedPilotBuild` produces artifacts whose names include `UNSIGNED-PILOT`.

The installer project pins WiX Toolset SDK 6.0.2. Confirm organizational acceptance of the applicable Open Source Maintenance Fee terms before building or distributing the MSI. A future WiX major-version change requires a new support, security, and license review.

## First installation

1. Verify the file name, version, SHA-256, Authenticode publisher, and transfer provenance.
2. Malware-scan the release with the target organization’s approved tooling.
3. Install the MSI through the approved path, or copy the portable EXE to its approved final folder.
4. Launch as the designated standard-user Windows profile and run **Settings → Run readiness check**. Application data is per user; do not switch operator accounts unless separate data stores are intended.
5. Configure Completed PDF, Excel, Adobe, and backup locations; save settings.
6. Create a manual full backup and confirm the `.zip` and `.sha256` files appear at the approved destination.
7. Complete the synthetic acceptance tests in `FIELD-TEST-CHECKLIST.md` before using operational data.

## Offline update

1. Finish or recover all in-progress intake/closeout operations.
2. Create and verify a full backup. Copy it to approved media/storage separate from the workstation when possible.
3. Close Equipment Tracking Platform and Adobe windows using its working PDFs.
4. Verify the newer release signature and SHA-256, and confirm its first three numeric MSI version fields are newer than the installed MSI.
5. For MSI deployment, install the newer MSI; Windows Installer performs the major upgrade. For portable deployment, retain the prior signed EXE as a rollback package and replace the old EXE in its approved folder.
6. Launch, run readiness checks, confirm the displayed version, inspect Recovery, and perform a synthetic open/search/print cycle.

Application updates do not intentionally delete or relocate data below `%LOCALAPPDATA%\58SOW\EquipmentTrackingPlatform` or the configured Documents folders for the designated user profile.

## Rollback

Do not simply install an older MSI over a newer one; downgrades are blocked. Preserve the prior package, current release hashes, and the pre-update full backup. If rollback is approved, uninstall the newer payload, install the prior approved package, and restore data only if both its database schema and backup format are supported by that older version. A newer unsupported schema/backup format is refused rather than silently modified. Prefer the backup created by the prior version immediately before the update when rehearsing rollback.

## Backup execution assumption

The application itself performs Monday 09:00 full and Monday–Friday 16:00 differential backups. Keep the application open at those times for exact execution. If it was closed, the latest missed run catches up when it next opens; a launch before 09:00 Monday can still catch up the prior Friday differential before the new weekly full. This design does not create a Windows service or scheduled task and does not run while no user session/application is active.
