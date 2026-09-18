# Equipment Tracking Platform 0.9.5-alpha.1

## Pickup viewing and printing fixes

- Opening a current 1297 from Dashboard or Equipment status now shows every completed pickup crossed out. Dashboard printing includes those marks on both readable copies of the Letter sheet.
- Documents now offers **View selected**, **Print readable copies**, and **Open preserved PDF**. A pickup receipt shows only its own selected devices; the current-status row shows all completed pickups, including archived records.
- Existing 0.9.4 pickups work without migration or rewriting any signed PDF. Original signed evidence, receipt links, database paths, and signature verification remain unchanged. Readable reference copies retain signature appearances but are not digitally signed replacements.

## Verification

149 automated tests passed, zero failed/skipped. The full unsigned release gate passed validation, application/installer dependency audits, offline review, both publish modes, and MSI creation. Synthetic PDF renders verified readable text and correct cross-outs on both copies. See `VALIDATION.md` for artifact hashes and remaining acceptance checks.

Application `0.9.5-alpha.1`; MSI `0.9.5`; database schema `6`. This is an **unsigned synthetic-data pilot build**, not authorization for operational records. Physical printer, Adobe/CAC, and disposable-machine MSI installation/upgrade acceptance remain outstanding. The build has not been installed; this release does not establish operational authorization.
