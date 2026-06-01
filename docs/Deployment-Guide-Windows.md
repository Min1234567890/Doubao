# Windows Enterprise Deployment Guide

## Prerequisites
- Windows 10/11 Enterprise or Windows Server host hardened against the CIS Microsoft Windows Benchmark.
- .NET Desktop Runtime 7.0 or a self-contained published build.
- SQL Server hardened against CIS Microsoft SQL Server Benchmark.
- Domain-managed users and groups for Admin, Operator, and Viewer roles.
- Trusted internal certificate authority for SQL Server TLS.

## Build
```powershell
dotnet build VehicleInspectionSuite.sln -c Release
```

## Database
1. Run `database/001_create_schema.sql` as a database administrator.
2. Replace placeholder secrets in `database/002_security_hardening.sql` with vault-generated values.
3. Run `database/002_security_hardening.sql`.
4. Run `database/003_seed_sample_data.sql` only in development or staging.
5. Create SQL users mapped to Windows service accounts and add them only to required roles.

## Application Deployment
1. Publish the WPF project for Windows x64.
2. Code-sign binaries with the enterprise signing certificate.
3. Install to a protected directory such as `C:\Program Files\UVSS\VehicleInspection`.
4. Grant write access only to the approved log/export directory.
5. Block direct clipboard/file export through Windows DLP policy where required.
6. Configure firewall rules to allow SQL/API endpoints only from approved hosts.

## Operations
- Review `AuditLog` and SQL Server Audit outputs daily or forward them to SIEM.
- Test session lock, Viewer export denial, Operator export audit, and Admin configuration access during acceptance testing.
- Validate encrypted backup restore at least quarterly.
- Rotate encryption certificates and service account credentials according to enterprise policy.
