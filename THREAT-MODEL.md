# Threat Model

## Assets

- Signed and archived 1297 PDFs
- Temporary two-copy print sheets and printed paper copies
- Customer and technician identity data
- Ticket, serial, model, organization, and status records
- SQLite database and Excel export
- Full/differential backup archives, manifests, sidecar hashes, and restore staging
- Signed MSI/portable EXE release artifacts and rollback packages
- Audit history and application configuration

## Trust boundaries

1. Technician and customer input through the WPF interface
2. USB/keyboard-wedge barcode scanner input
3. Smart-card readers and Windows smart-card providers
4. Adobe Acrobat/Reader and the PDF template
5. Local filesystem, SQLite, and Excel output
6. Build-time NuGet feeds and third-party packages
7. Adobe/Windows print handling, printer drivers, physical printers, and paper custody
8. Backup destinations, removable/secondary volumes, and restore operators
9. Offline software-transfer media and Windows Installer/application-control boundary

## Primary threats and mitigations

- **Malicious or malformed scanner data:** bounded input, parser fallbacks, required-field checks, no command execution, parameterized SQL, raw value capped at 4096 characters.
- **Duplicate or mistyped serial numbers:** case-insensitive in-memory check, database lookup, and partial unique index for active devices.
- **Path traversal or unsafe organization names:** generated folder component removes invalid characters, reserved names, leading/trailing dots, and excessive length.
- **Partial/corrupt PDF archive copy:** copy to a temporary file, flush to disk, compare SHA-256, then atomically rename and update SQLite transactionally.
- **Stale CAC certificates:** certificates are obtained from card-present reader stores rather than the cached CurrentUser certificate store.
- **Private-key/PIN exposure:** no PIN prompt or private-key operation; only public certificate bytes and display metadata are read.
- **SQL injection:** all data values use SQLite parameters; dynamic SQL is limited to internal allow-listed chart expressions and migration columns.
- **Log injection/data leakage:** control characters removed, values bounded, stack traces and certificate details omitted, rotation and retention applied.
- **Vulnerable dependencies:** direct/transitive NuGet audit, build failure on known vulnerability warnings, approved-feed support, Windows-serviced SQLite native library.
- **Unauthorized local access:** relies on Windows authentication, user-profile ACLs, endpoint encryption, application control, and least-privilege execution.
- **Tampering after PDF signature:** the signed PDF is preserved as the authoritative artifact. Any later field changes can be identified by Adobe; closeout requires the configured Pickup Signature field.
- **Print operation alters the record:** composition is performed into a new local print-job PDF, the tracked source hash is checked before and after generation, and no database/archive path is changed.
- **Temporary print-copy disclosure:** print sheets remain under the current user's profile ACL, are excluded from support packages, and are deleted after the printing notice closes. Bounded retry and next-startup cleanup cover viewer locks or an interrupted session; pilot reset also removes them. Printed pages remain subject to the data owner's physical handling and disposal rules.
- **Corrupt or incomplete backup:** SQLite online backup creates a consistent database copy; official database paths must resolve to normal PDF files below the configured record root; archives are closed and reopened; database/settings/PDF hashes and `PRAGMA quick_check` must pass before acceptance.
- **Backup deletion leaves no recovery chain:** retention independently re-verifies the new full before prior-week differentials are removed. If creation/verification/retention fails, prior material remains and the scheduler retries while open.
- **Differential loses a moved/deleted record:** differentials carry deletion markers relative to the Monday full; staged merge applies them before the changed-file overlay. Restore to an existing record folder remains non-destructive and can leave unrelated files for manual records review.
- **Differential restored without its base:** restore requires the identified Monday full in the same folder and refuses to stage when it is absent or mismatched.
- **Malicious restore archive:** only named entries are extracted, duplicate entries and unsafe paths are rejected, file counts/manifest size are bounded, hashes are checked, schema versions are bounded, and restore is staged before replacement.
- **Drive failure defeats backup:** readiness warns when the backup shares a drive/share with either the database or records. Operational policy must provide an approved separate destination where drive-loss protection is required.
- **Backup disclosure or malicious replacement:** archive hashes detect accidental change but do not provide application-level encryption or a trusted digital signature. Approved storage encryption, ACLs, media custody, retention, and restore-operator controls remain required.
- **Unauthorized or tampered update:** the attended local-MSI command checks ProductName, stable UpgradeCode, strictly newer numeric version, offline WinVerifyTrust result, and companion-manifest SHA-256/size when present; it shows unsigned/missing-manifest warnings, requires explicit confirmation/UAC, creates a verified backup, and never retrieves a package. Operators still verify approved transfer provenance and publisher, while application control limits execution to the approved release.
- **MSI release is not recognized as newer:** the build/deployment process requires one of the first three numeric MSI version fields to change and refuses same-version major upgrades.
- **Portable single-file extraction blocked or abused:** use an approved final folder and endpoint rules; prefer the MSI’s conventional Program Files payload on managed systems.

## Out of scope for this alpha

- Multi-user authorization and concurrent shared-database access
- Certificate trust/revocation validation
- Centralized audit collection or SIEM integration
- Application-layer database encryption
- Application-layer backup encryption or backup-manifest digital signatures
- Automatic/network software update or remote service communication
- Backup execution while the application is closed or the user is logged off
