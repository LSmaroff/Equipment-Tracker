# DISA STIG Readiness Notes

This document is an engineering crosswalk, not a completed checklist or compliance attestation. The application owner must use the current DISA Application Security and Development STIG/SRG and any locally assigned controls.

| Security area | Current implementation | Deployment or assessment responsibility |
|---|---|---|
| Least privilege | Manifest requests `asInvoker`; no administrative operation is required. | Confirm install location, file ACLs, and application-control policy. |
| Authentication | Application relies on the logged-on Windows session. CAC certificates are read only from presently inserted smart cards for identity convenience. | Confirm workstation authentication and session-lock policy. The application does not replace Windows authentication. |
| Authorization | Single-user workstation alpha; no in-app roles. | Limit access with Windows accounts, ACLs, software distribution, and local policy. Add application roles before multi-user or shared-service deployment. |
| Input validation | Length limits, required fields, allow-listed statuses, safe filename normalization, PDF-field mapping validation, parameterized SQL. | Test organization-specific scanners and malformed files. |
| Output encoding and file handling | Windows-invalid/reserved names blocked; archive path is generated from a normalized organization component; PDF copies are hash-verified. | Approve output locations and retention. |
| Database security | Local SQLite, foreign keys, transactions, uniqueness constraints, audit records, WAL mode, `trusted_schema=OFF`. | Protect the user profile and backups with endpoint encryption and ACLs. SQLite is not an approved multi-user network database. |
| Cryptography | Uses Windows/.NET SHA-256 for copy-integrity checks. CAC private keys and PINs are never accessed. Signature trust/revocation validation is intentionally out of scope. | Confirm whether formal signature validation is required by policy; this workflow only requires evidence that the PDF field was signed. |
| Audit and accountability | Database audit table records transaction/status actions, technician, workstation, and time. Logs are bounded and control-character sanitized. | Define retention, central collection, review frequency, and protection from modification. |
| Error handling | User receives bounded messages; logs contain exception type/message but no stack trace or certificate/raw-scan dump. | Review logs for operational sensitivity and approved retention. |
| Dependency management | .NET 10 NuGet audit covers transitive dependencies; vulnerability warnings are errors; security scan script creates a report. | Use an approved package mirror, preserve build evidence, patch .NET/Windows/Adobe, and rescan before release. |
| Network security | No inbound listener, telemetry, cloud API, or runtime Internet call. | Confirm firewall/application-control behavior during deployment. |
| Configuration management | Versioned source, deterministic builds, validation scripts, changelog, checksums, and release archives. | Sign release artifacts and maintain approved baseline/hash records. |
| Sensitive-data minimization | Certificate subject/thumbprint are not persisted for new records; raw scans remain in SQLite but are omitted from Excel. | Confirm data elements, marking, retention, and disposal with the data owner. |
| Availability/recovery | Atomic settings write, transactional database changes, verified PDF copies. | Establish approved backups, restore testing, and continuity procedures. |

## Open findings before production authorization

1. There is no in-application role-based access control. It relies on the Windows user and file permissions.
2. SQLite data is not application-layer encrypted. Endpoint full-disk encryption and profile ACLs are required unless the authorizing official requires a different design.
3. This is a single-workstation architecture. Do not place the SQLite database on a shared network drive for concurrent use.
4. The code has not been evaluated by DISA, an AFNET software approval authority, or a third-party assessor.
5. Adobe, CAC middleware, Windows, .NET runtime, scanner firmware, and the exact PDF template remain part of the assessed system boundary.
6. The app detects a CMS signature in the configured field but does not perform certificate-chain, revocation, or trusted-timestamp validation by design.
7. Reader-bound CAC enumeration uses Windows smart-card CSP/provider APIs for compatibility. Microsoft identifies portions of the older CryptoAPI surface as deprecated, so the exact approved middleware/readers must be tested and the implementation revisited if the target environment requires a CNG-only design.

## Assessment references

Use the current versions available from the official sources during assessment:

- DISA STIG and SRG library: `https://public.cyber.mil/stigs/`
- DISA STIG downloads: `https://www.cyber.mil/stigs/downloads`
- DISA STIG FAQ: `https://public.cyber.mil/stigs/faqs/`
- DoD Enterprise DevSecOps Fundamentals and Activities/Tools guidance: `https://dodcio.defense.gov/library/`

The DISA FAQ states that all DoD applications are subject to the Application Security and Development STIG. Applicability and finding disposition must still be determined by the responsible assessor and authorizing chain for the actual deployment.
