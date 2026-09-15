# Equipment Tracking Platform 0.9.4-alpha.1

## What's new

- Signed partial pickups: scan or check several devices on one 1297, prepare one pickup copy that crosses out only those devices, and finalize after the customer signs. Original intake and all pickup receipts remain grouped under the same record; the final pickup automatically archives it.
- Improved offline recognition for the supplied HP EliteBook 645 / A4TH1AV identity, plus conservative exact-history matching and editable part/model catalog resolution. Existing check-in stays unchanged.
- Stronger pickup recovery, migration, backup/restore, and status-transition safeguards. Existing customer-name handling, model-name spaces, Dashboard scan-to-search, and pie-chart hover remain included.
- Complete application source, tests, installer authoring, scripts, approved cleaned template, documentation, and AI handoff files are now included in the repository. Start with `AGENTS.md`, `docs/AI_HANDOFF.md`, and `docs/PROJECT_STATUS.md`.

## Downloads

- `EquipmentTrackingPlatform-0.9.4-alpha.1-win-x64-UNSIGNED-PILOT.msi`: machine-wide Windows x64 installer.
- `EquipmentTrackingPlatform-0.9.4-alpha.1-win-x64-UNSIGNED-PILOT.exe`: portable self-contained Windows x64 executable.
- `release-manifest.json` and `SHA256SUMS.txt`: release metadata and integrity checks.
- GitHub's source archives contain the complete tagged source tree, including AI handoff documents.

## Verification and limitations

The canonical release gate passed: 145 tests, zero failed, zero skipped; repository validation, dependency vulnerability audits, offline-runtime review, both publish modes, MSI build, and artifact checksums passed. Application `0.9.4-alpha.1`; MSI `0.9.4`; database schema `6`.

This is an **unsigned synthetic-data pilot pre-release**, not organizational authorization for operational records. Windows may show Unknown Publisher. Physical scanner, Adobe/CAC signing, target DPI/theme, and disposable-machine MSI install/upgrade/rollback acceptance remain outstanding. See `VALIDATION.md` for exact evidence and limits.
