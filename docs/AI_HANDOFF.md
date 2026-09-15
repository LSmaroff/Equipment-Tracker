# AI and developer handoff

Start with [../AGENTS.md](../AGENTS.md), then read [PROJECT_STATUS.md](PROJECT_STATUS.md) in full. Those files contain repository rules, architecture, workflow guarantees, verification evidence, and outstanding acceptance work. The checked-out source is authoritative; do not replace it with an older release archive or generated output.

## Published baseline

- Repository: https://github.com/LSmaroff/Equipment-Tracker
- Branch: `main`; release tag: `v0.9.4-alpha.1`.
- Application `0.9.4-alpha.1`, MSI `0.9.4`, SQLite schema `6`.
- Canonical unsigned release gate: 145 tests passed, zero failed/skipped; validation, dependency audits, offline-runtime review, both EXE publish modes, and MSI creation passed.
- Exact artifact hashes, MSI identity, limitations, and historical evidence: [../VALIDATION.md](../VALIDATION.md).
- Feature history: [../CHANGELOG.md](../CHANGELOG.md); user instructions: [../README.md](../README.md).

## Preserve these behaviors

New Intake customer text remains authoritative independently of signer identity. Model names support spaces, Dashboard typing/scanning routes to search, and pie-chart hover highlights remain intact. Existing check-in and full closeout workflows are unchanged.

Partial pickup selects/scans multiple devices from one 1297, prepares a separate copy from the preserved signed intake, crosses out only that pickup's selected devices, and requires a new verified Pickup Signature before marking them Returned. All signed receipts stay beneath the same parent record; the final subset archives the parent. Preserve transactional persistence, recovery, original signed documents, audit evidence, migration, backup, and restore protections.

Recognition stays offline and conservative: exact known identity, unambiguous exact serial history, and operator-maintained part/model catalog. Never infer a device model from a serial prefix alone.

## Next verification work

Use synthetic data for physical scanner timing, real Adobe/CAC signing, target DPI/theme checks, and disposable-machine MSI install/upgrade/rollback tests. These physical acceptance checks have not been completed by the automated gate. Do not modify operational records or claim organizational approval.

## Build and publication

From the root in Windows PowerShell, run `./scripts/build-release.ps1 -AllowUnsignedPilotBuild`. Do not bypass tests, validation, security, offline review, signatures, or backup safeguards to obtain a pass.

Release assets contain the verified MSI, portable EXE, manifest, and SHA-256 checksums. Generated build output and local QA transcripts are intentionally excluded from Git; the source-controlled validation record contains their results. No private AI chat/session exports, credentials, operational databases, or operational PDFs belong in this repository. Only the approved cleaned PDF template is included.

The unsigned build is `UNSIGNED SYNTHETIC-DATA PILOT ONLY`. Publishing it does not establish operational authorization. Any future commit, push, or release publication requires user authorization under AGENTS.md.
