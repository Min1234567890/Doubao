# CIS Compliance Validation Report

## Status
This solution is CIS-aligned by design, but it is not a substitute for a formal CIS Level 2 certification audit. Level 2 compliance must be validated on the target Windows endpoints, SQL Server hosts, network, identity provider, certificates, service accounts, backup system, and deployment pipeline.

## Application Controls
- Role-based access control: Admin, Operator, Viewer in `AccessControlService`.
- Export restriction: Viewer cannot export CSV/PDF manifests.
- Audit logging: dashboard view, report search, export success, and export denial are recorded through `AuditService`.
- Idle lock foundation: `SessionLockService` defines a 15-minute lock threshold.
- No plaintext credentials: generated code contains no database password or hard-coded user secret.
- Secure error handling: unauthorized export shows a user-safe message without stack traces or system paths beyond the chosen export path.
- Data copying: export actions are permission-gated and audited; production builds should add clipboard/DLP policy enforcement through Windows enterprise policy.

## .NET Backend / Application Tier Controls
- Business logic is isolated in `VehicleInspection.Application`.
- Data access is behind repository interfaces in `VehicleInspection.Data`.
- SQL implementation must use parameterized queries or EF Core parameters only.
- TLS 1.2+ is required for any API/database/network service communication.
- Logging must avoid secrets, raw credentials, and unmasked sensitive fields.

## MSSQL Controls
- Least privilege roles are defined in `database/002_security_hardening.sql`.
- Audit tables exist in `database/001_create_schema.sql`.
- SQL Server Audit specification is included for database access and permission changes.
- License plate encryption design uses AES-256 symmetric key protected by certificate.
- License plate search should use normalized SHA-256 hash, not decrypted plate text.
- Indexes support date, status, plate hash, FOD severity, and audit review workflows.

## Required Environment Validation
1. Disable or rename `sa`; enforce CHECK_POLICY and CHECK_EXPIRATION for SQL logins.
2. Revoke guest access from user databases where not explicitly required.
3. Force TLS 1.2+ using trusted certificates for SQL Server and any API services.
4. Run Windows endpoints against CIS Microsoft Windows Benchmarks.
5. Use least-privilege Windows service accounts with no interactive login.
6. Enable SQL Server encrypted backups and verify restore tests.
7. Store secrets in an enterprise vault or Windows DPAPI-protected store.
8. Code-sign desktop binaries and enforce application allowlisting.
9. Configure centralized log forwarding/SIEM retention for audit events.
10. Perform SAST, dependency scanning, and a formal CIS control review before production.
