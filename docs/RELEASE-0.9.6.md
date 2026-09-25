# Equipment Tracking Platform 0.9.6-alpha.1

After finalizing an intake, technicians can now select **Print two 1297 copies** directly in Intake. The completed ticket is displayed, and the button opens the existing readable two-copy Letter print sheet without navigating to Dashboard.

- Reprint without finalizing again. Printing errors leave the saved record intact.
- The shortcut becomes available only after successful finalization and clears when starting/resetting an intake. Busy and double-click protection prevents overlapping print jobs.
- Existing readable formatting, pickup cross-outs, signed-original preservation, and temporary print cleanup remain unchanged.

Application `0.9.6-alpha.1`; MSI `0.9.6`; database schema `6` (no migration). Source, tests, build scripts, installer authoring, and AI handoff documents are included in the tagged source tree. Release assets include the MSI, portable EXE, manifest, and SHA-256 checksums.

This is an **unsigned synthetic-data pilot pre-release**, not operational authorization. Physical printer/Adobe/CAC and disposable-machine MSI installation/upgrade acceptance remain outstanding. See `VALIDATION.md` for release verification evidence.

The canonical release gate passed all 154 tests (0 failed/skipped), repository validation, dependency audits, offline-runtime review, both publish modes, and MSI creation. Uploaded assets are checked against the verified local SHA-256 values before publication.
