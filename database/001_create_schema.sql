CREATE DATABASE VehicleInspection;
GO

USE VehicleInspection;
GO

CREATE TABLE dbo.Roles (
    RoleId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
    RoleName NVARCHAR(32) NOT NULL CONSTRAINT UQ_Roles_RoleName UNIQUE
);
GO

CREATE TABLE dbo.Users (
    UserId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Users PRIMARY KEY DEFAULT NEWID(),
    UserName NVARCHAR(128) NOT NULL CONSTRAINT UQ_Users_UserName UNIQUE,
    DisplayName NVARCHAR(128) NOT NULL,
    IsEnabled BIT NOT NULL CONSTRAINT DF_Users_IsEnabled DEFAULT 1,
    CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Users_CreatedUtc DEFAULT SYSUTCDATETIME()
);
GO

CREATE TABLE dbo.UserRoles (
    UserId UNIQUEIDENTIFIER NOT NULL,
    RoleId INT NOT NULL,
    CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId),
    CONSTRAINT FK_UserRoles_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId),
    CONSTRAINT FK_UserRoles_Roles FOREIGN KEY (RoleId) REFERENCES dbo.Roles(RoleId)
);
GO

CREATE TABLE dbo.Inspections (
    InspectionId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Inspections PRIMARY KEY DEFAULT NEWID(),
    ScanTimeUtc DATETIME2(3) NOT NULL,
    LicensePlateCipher VARBINARY(512) NULL,
    LicensePlateHash VARBINARY(32) NOT NULL,
    InspectionStatus NVARCHAR(32) NOT NULL,
    Lane NVARCHAR(64) NOT NULL,
    OperatorUserId UNIQUEIDENTIFIER NULL,
    SystemHealth NVARCHAR(256) NOT NULL,
    CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Inspections_CreatedUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Inspections_Users FOREIGN KEY (OperatorUserId) REFERENCES dbo.Users(UserId),
    CONSTRAINT CK_Inspections_Status CHECK (InspectionStatus IN ('Pending','Clear','Review','Hold','Escalated'))
);
GO

CREATE TABLE dbo.InspectionImages (
    ImageId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_InspectionImages PRIMARY KEY DEFAULT NEWID(),
    InspectionId UNIQUEIDENTIFIER NOT NULL,
    ImageType NVARCHAR(32) NOT NULL,
    StorageUri NVARCHAR(512) NOT NULL,
    Sha256Hash VARBINARY(32) NOT NULL,
    CapturedUtc DATETIME2(3) NOT NULL,
    CONSTRAINT FK_InspectionImages_Inspections FOREIGN KEY (InspectionId) REFERENCES dbo.Inspections(InspectionId),
    CONSTRAINT CK_InspectionImages_Type CHECK (ImageType IN ('UnderVehicle','FullVehicle','LicensePlate','Xray'))
);
GO

CREATE TABLE dbo.FodAlerts (
    FodAlertId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_FodAlerts PRIMARY KEY DEFAULT NEWID(),
    InspectionId UNIQUEIDENTIFIER NOT NULL,
    Zone NVARCHAR(64) NOT NULL,
    Severity NVARCHAR(32) NOT NULL,
    Description NVARCHAR(512) NOT NULL,
    Confidence DECIMAL(5,4) NOT NULL,
    CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_FodAlerts_CreatedUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_FodAlerts_Inspections FOREIGN KEY (InspectionId) REFERENCES dbo.Inspections(InspectionId),
    CONSTRAINT CK_FodAlerts_Severity CHECK (Severity IN ('Low','Medium','High','Critical')),
    CONSTRAINT CK_FodAlerts_Confidence CHECK (Confidence >= 0 AND Confidence <= 1)
);
GO

CREATE TABLE dbo.OperatorNotes (
    NoteId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_OperatorNotes PRIMARY KEY DEFAULT NEWID(),
    InspectionId UNIQUEIDENTIFIER NOT NULL,
    UserId UNIQUEIDENTIFIER NOT NULL,
    NoteText NVARCHAR(2000) NOT NULL,
    CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_OperatorNotes_CreatedUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_OperatorNotes_Inspections FOREIGN KEY (InspectionId) REFERENCES dbo.Inspections(InspectionId),
    CONSTRAINT FK_OperatorNotes_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId)
);
GO

CREATE TABLE dbo.AuditLog (
    AuditLogId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AuditLog PRIMARY KEY DEFAULT NEWID(),
    EventTimeUtc DATETIME2(3) NOT NULL CONSTRAINT DF_AuditLog_EventTimeUtc DEFAULT SYSUTCDATETIME(),
    UserName NVARCHAR(128) NOT NULL,
    RoleName NVARCHAR(32) NOT NULL,
    ActionName NVARCHAR(128) NOT NULL,
    Target NVARCHAR(512) NOT NULL,
    Result NVARCHAR(64) NOT NULL,
    Workstation NVARCHAR(128) NOT NULL,
    ClientIp NVARCHAR(64) NULL
);
GO

CREATE TABLE dbo.ExportLog (
    ExportLogId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ExportLog PRIMARY KEY DEFAULT NEWID(),
    AuditLogId UNIQUEIDENTIFIER NOT NULL,
    ExportType NVARCHAR(32) NOT NULL,
    FileHash VARBINARY(32) NULL,
    RecordCount INT NOT NULL,
    CreatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_ExportLog_CreatedUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_ExportLog_AuditLog FOREIGN KEY (AuditLogId) REFERENCES dbo.AuditLog(AuditLogId)
);
GO

CREATE TABLE dbo.SystemStatus (
    SystemStatusId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SystemStatus PRIMARY KEY,
    EventTimeUtc DATETIME2(3) NOT NULL CONSTRAINT DF_SystemStatus_EventTimeUtc DEFAULT SYSUTCDATETIME(),
    ComponentName NVARCHAR(128) NOT NULL,
    HealthState NVARCHAR(32) NOT NULL,
    Detail NVARCHAR(512) NOT NULL
);
GO

CREATE INDEX IX_Inspections_ScanTimeUtc ON dbo.Inspections(ScanTimeUtc DESC);
CREATE INDEX IX_Inspections_LicensePlateHash ON dbo.Inspections(LicensePlateHash);
CREATE INDEX IX_Inspections_Status ON dbo.Inspections(InspectionStatus);
CREATE INDEX IX_FodAlerts_InspectionSeverity ON dbo.FodAlerts(InspectionId, Severity);
CREATE INDEX IX_AuditLog_EventTimeUtc ON dbo.AuditLog(EventTimeUtc DESC);
CREATE INDEX IX_AuditLog_UserAction ON dbo.AuditLog(UserName, ActionName, EventTimeUtc DESC);
GO
