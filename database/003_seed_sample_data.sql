USE VehicleInspection;
GO

INSERT INTO dbo.Roles (RoleName) VALUES ('Admin'), ('Operator'), ('Viewer');
GO

DECLARE @Operator UNIQUEIDENTIFIER = NEWID();
INSERT INTO dbo.Users (UserId, UserName, DisplayName) VALUES (@Operator, 'DOMAIN\\uvss.operator', 'UVSS Operator');
INSERT INTO dbo.UserRoles (UserId, RoleId) SELECT @Operator, RoleId FROM dbo.Roles WHERE RoleName = 'Operator';

DECLARE @Inspection UNIQUEIDENTIFIER = NEWID();
INSERT INTO dbo.Inspections (InspectionId, ScanTimeUtc, LicensePlateCipher, LicensePlateHash, InspectionStatus, Lane, OperatorUserId, SystemHealth)
VALUES (@Inspection, SYSUTCDATETIME(), NULL, HASHBYTES('SHA2_256', 'SEC-2048'), 'Review', 'Gate A / Lane 02', @Operator, 'All sensors online');

INSERT INTO dbo.InspectionImages (InspectionId, ImageType, StorageUri, Sha256Hash, CapturedUtc)
VALUES
(@Inspection, 'UnderVehicle', '\\secure-share\\uvss\\uv\\SEC-2048.png', HASHBYTES('SHA2_256', 'uv'), SYSUTCDATETIME()),
(@Inspection, 'FullVehicle', '\\secure-share\\uvss\\full\\SEC-2048.png', HASHBYTES('SHA2_256', 'full'), SYSUTCDATETIME()),
(@Inspection, 'LicensePlate', '\\secure-share\\uvss\\plate\\SEC-2048.png', HASHBYTES('SHA2_256', 'plate'), SYSUTCDATETIME());

INSERT INTO dbo.FodAlerts (InspectionId, Zone, Severity, Description, Confidence)
VALUES (@Inspection, 'Rear axle', 'High', 'Foreign object detected near exhaust line', 0.9400);

INSERT INTO dbo.OperatorNotes (InspectionId, UserId, NoteText)
VALUES (@Inspection, @Operator, 'Vehicle held for secondary inspection.');
GO
