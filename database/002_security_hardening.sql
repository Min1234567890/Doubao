USE VehicleInspection;
GO

CREATE ROLE VehicleInspection_App_Read;
CREATE ROLE VehicleInspection_App_Write;
CREATE ROLE VehicleInspection_Audit_Read;
CREATE ROLE VehicleInspection_Admin;
GO

GRANT SELECT ON dbo.Inspections TO VehicleInspection_App_Read;
GRANT SELECT ON dbo.InspectionImages TO VehicleInspection_App_Read;
GRANT SELECT ON dbo.FodAlerts TO VehicleInspection_App_Read;
GRANT SELECT ON dbo.OperatorNotes TO VehicleInspection_App_Read;
GRANT SELECT ON dbo.SystemStatus TO VehicleInspection_App_Read;

GRANT INSERT, UPDATE ON dbo.OperatorNotes TO VehicleInspection_App_Write;
GRANT INSERT ON dbo.AuditLog TO VehicleInspection_App_Write;
GRANT INSERT ON dbo.ExportLog TO VehicleInspection_App_Write;

GRANT SELECT ON dbo.AuditLog TO VehicleInspection_Audit_Read;
GRANT SELECT ON dbo.ExportLog TO VehicleInspection_Audit_Read;

GRANT CONTROL ON DATABASE::VehicleInspection TO VehicleInspection_Admin;
GO

DENY SELECT ON dbo.Users TO VehicleInspection_App_Read;
DENY SELECT ON dbo.UserRoles TO VehicleInspection_App_Read;
GO

CREATE MASTER KEY ENCRYPTION BY PASSWORD = 'REPLACE_WITH_ENTERPRISE_SECRET_FROM_SECURE_VAULT';
GO

CREATE CERTIFICATE VehicleInspectionDataCertificate
    WITH SUBJECT = 'VehicleInspection license plate encryption certificate';
GO

CREATE SYMMETRIC KEY VehicleInspectionLicensePlateKey
    WITH ALGORITHM = AES_256
    ENCRYPTION BY CERTIFICATE VehicleInspectionDataCertificate;
GO

CREATE SERVER AUDIT VehicleInspectionServerAudit
TO FILE (FILEPATH = 'D:\\SqlAudit\\VehicleInspection\\', MAXSIZE = 512 MB, MAX_ROLLOVER_FILES = 20)
WITH (QUEUE_DELAY = 1000, ON_FAILURE = FAIL_OPERATION);
GO

ALTER SERVER AUDIT VehicleInspectionServerAudit WITH (STATE = ON);
GO

CREATE DATABASE AUDIT SPECIFICATION VehicleInspectionDatabaseAudit
FOR SERVER AUDIT VehicleInspectionServerAudit
ADD (SELECT, INSERT, UPDATE, DELETE ON DATABASE::VehicleInspection BY public),
ADD (DATABASE_PERMISSION_CHANGE_GROUP),
ADD (DATABASE_ROLE_MEMBER_CHANGE_GROUP),
ADD (SCHEMA_OBJECT_PERMISSION_CHANGE_GROUP)
WITH (STATE = ON);
GO

-- CIS operations checklist for DBA execution:
-- 1. Disable or rename sa account and enforce CHECK_POLICY for SQL logins.
-- 2. Revoke CONNECT from guest in all user databases where not required.
-- 3. Force TLS 1.2+ with a trusted certificate in SQL Server Network Configuration.
-- 4. Restrict SQL Server to an isolated management/application VLAN.
-- 5. Enable encrypted backups and test restore procedures.
-- 6. Use Windows service accounts with least privilege and no interactive login.
-- 7. Store application connection strings in enterprise secret storage, not source code.
