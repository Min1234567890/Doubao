# UVSS Vehicle Inspection Suite

Windows-native enterprise dashboard for vehicle security screening operations. The solution is built in C# with WPF and follows a 3-tier architecture for presentation, application logic, and data persistence.

The system is designed for 24/7 security operation centers where the undervehicle image is the primary inspection artifact. It supports undervehicle scan review, full vehicle imagery, license plate OCR context, optional X-ray imagery, FOD alert review, bilingual English/Chinese UI, report filtering, export workflows, RBAC, audit logging, and MSSQL hardening guidance.

## Solution Overview

```text
VehicleInspectionSuite.sln
├── src/
│   ├── VehicleInspection.App/          # WPF Windows desktop presentation tier
│   ├── VehicleInspection.Application/  # Business logic, RBAC, audit, export services
│   └── VehicleInspection.Data/         # Repository implementations and data access boundary
├── database/                           # MSSQL schema, hardening, seed scripts
├── docs/                               # CIS validation and Windows deployment guides
├── index.html                          # Earlier UVSS web prototype, not used by WPF app
├── styles.css                          # Earlier UVSS web prototype, not used by WPF app
└── script.js                           # Earlier UVSS web prototype, not used by WPF app
```

## Architecture

The application follows a 3-tier enterprise architecture.

### 1. Presentation Tier: `VehicleInspection.App`

WPF desktop application targeting `net7.0-windows`.

Responsibilities:
- Render the dark UVSS-branded security dashboard.
- Keep the undervehicle image panel as the dominant visual focus.
- Provide zoom and pan for all image panels.
- Show secondary panels for full vehicle image, license plate OCR, and X-ray/FOD logic.
- Provide the inspection report page with filters and exports.
- Support one-click English/Chinese language switching.
- Enforce UI-level RBAC affordances such as disabling unauthorized export flows.

Key modules:

```text
src/VehicleInspection.App/
├── App.xaml
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── Controls/
│   ├── ZoomPanImageControl.xaml
│   └── ZoomPanImageControl.xaml.cs
├── Resources/
│   ├── Theme.xaml
│   ├── Strings.en-US.xaml
│   └── Strings.zh-CN.xaml
├── ViewModels/
│   ├── DashboardViewModel.cs
│   ├── MainViewModel.cs
│   ├── RelayCommand.cs
│   ├── ReportViewModel.cs
│   └── ViewModelBase.cs
└── Views/
    ├── DashboardView.xaml
    ├── DashboardView.xaml.cs
    ├── ReportView.xaml
    └── ReportView.xaml.cs
```

#### `MainWindow`

The main shell contains:
- Top bar with UVSS branding.
- Scan time.
- inspection status.
- language switch.
- user and role display.
- lock button.
- navigation between Dashboard and Reports.

`MainWindow.xaml.cs` wires:
- initial view model loading.
- active view switching.
- runtime resource dictionary replacement for English/Chinese text.

#### `DashboardView`

The operations dashboard is arranged around the inspection workflow:

- Large primary undervehicle image panel.
- Full vehicle image panel.
- License plate OCR panel.
- Conditional X-ray/FOD panel.
- Inspection summary.
- System health.
- Operator notes.

The UV image panel intentionally receives the largest layout region, stronger border, and primary display label because UV imagery is the main security review surface.

#### `ReportView`

Dedicated report page for inspection history.

Features:
- Date range filter.
- License plate filter.
- Inspection status filter.
- FOD-alert-only filter.
- Data grid with historical inspections.
- CSV export.
- PDF manifest export placeholder.
- RBAC-gated export commands.
- Export audit logging through the application tier.

#### `ZoomPanImageControl`

Reusable WPF control for inspection image panels.

Features:
- Mouse wheel zoom.
- Mouse drag pan.
- Reset button.
- Lightweight vector inspection placeholder.
- Bitmap caching and frame-throttled zoom updates to keep the UI responsive.

Current implementation uses placeholder vectors so the solution builds without external image assets. Production integration should bind actual image sources from secure storage.

#### `Resources/Theme.xaml`

Central UVSS design system for WPF.

Palette:
- Deep navy: `#101820`
- Navy/charcoal: `#1A242F`
- Panel dark: `#162230`
- Corporate blue: `#1F6FBF`
- Bright technical blue: `#4EA3E6`
- Muted text gray: `#9EACBA`

The theme is optimized for security operations rooms and 24/7 monitoring.

#### `Strings.en-US.xaml` and `Strings.zh-CN.xaml`

Bilingual resource dictionaries used by `DynamicResource` bindings. The language toggle swaps these dictionaries at runtime.

### 2. Application Tier: `VehicleInspection.Application`

Class library targeting `net7.0`.

Responsibilities:
- Domain models.
- Inspection search and current inspection logic.
- RBAC permission checks.
- Audit logging orchestration.
- Export workflows.
- Session idle-lock logic.
- Repository interfaces.

Key modules:

```text
src/VehicleInspection.Application/
├── Models/
│   ├── AuditEntry.cs
│   ├── InspectionRecord.cs
│   ├── ReportFilter.cs
│   └── UserSession.cs
├── Repositories/
│   └── IInspectionRepository.cs
├── Security/
│   ├── AccessControlService.cs
│   ├── Permission.cs
│   └── Role.cs
└── Services/
    ├── AuditService.cs
    ├── ExportService.cs
    ├── InspectionService.cs
    └── SessionLockService.cs
```

#### Models

`InspectionRecord` represents one vehicle screening event:
- scan time.
- license plate.
- inspection status.
- UV image path.
- full vehicle image path.
- license plate image path.
- optional X-ray image path.
- FOD alerts.
- operator name.
- lane.
- notes.
- system health.

`FodAlert` records zone, severity, description, and confidence.

`ReportFilter` contains report query criteria.

`UserSession` tracks current user, role, login time, last activity, and lock state.

`AuditEntry` represents auditable user activity.

#### RBAC

`Role` supports:
- `Admin`
- `Operator`
- `Viewer`

`Permission` supports:
- dashboard viewing.
- report viewing.
- report export.
- note editing.
- configuration management.
- audit log viewing.

`AccessControlService` maps roles to permissions.

Current permissions:
- Viewer: dashboard and report viewing only.
- Operator: dashboard, reports, exports, and notes.
- Admin: all permissions.

#### `InspectionService`

Application service for inspection workflows.

Responsibilities:
- Load the current inspection.
- Search inspection history by filter.
- Audit dashboard views and report searches.

#### `AuditService`

Central audit logging service.

Records:
- dashboard views.
- report searches.
- export attempts.
- export denials.
- export successes.

The current implementation writes through the repository interface. Production deployments should persist audit entries to MSSQL and forward them to SIEM.

#### `ExportService`

Handles report exports with RBAC and audit logging.

Supported outputs:
- CSV export.
- PDF-ready manifest export.

The PDF path is intentionally implemented as a manifest placeholder because production PDF generation should use an enterprise-approved, signed PDF library. The audit and authorization workflow is already in place.

#### `SessionLockService`

Defines idle session-lock behavior. The default threshold is 15 minutes.

Production hardening should connect this service to global input tracking and require re-authentication through the enterprise identity provider.

### 3. Data Tier: `VehicleInspection.Data`

Class library targeting `net7.0`.

Responsibilities:
- Repository implementations.
- Data access boundary.
- Development sample data.

Key modules:

```text
src/VehicleInspection.Data/
└── Repositories/
    ├── DataRepositoryAssemblyMarker.cs
    └── InMemoryInspectionRepository.cs
```

#### `InMemoryInspectionRepository`

Development repository used to run the WPF app without requiring SQL Server during local UI work.

It provides:
- current inspection data.
- historical report data.
- sample X-ray and FOD conditions.
- in-memory audit log storage.

Production should replace or extend this with an MSSQL-backed repository using parameterized queries or EF Core parameterization.

## Database Module

SQL scripts are located in `database/`.

```text
database/
├── 001_create_schema.sql
├── 002_security_hardening.sql
└── 003_seed_sample_data.sql
```

### `001_create_schema.sql`

Creates the main MSSQL schema:
- `Roles`
- `Users`
- `UserRoles`
- `Inspections`
- `InspectionImages`
- `FodAlerts`
- `OperatorNotes`
- `AuditLog`
- `ExportLog`
- `SystemStatus`

Indexes support:
- scan time filtering.
- license plate hash lookup.
- inspection status lookup.
- FOD severity lookup.
- audit event review.

### `002_security_hardening.sql`

Adds CIS-aligned MSSQL controls:
- least-privilege database roles.
- grants for read/write/audit/admin roles.
- denial of direct user-role table reads to app read role.
- AES-256 symmetric key design for license plate encryption.
- SQL Server Audit and Database Audit Specification.
- DBA checklist for `sa`, guest, TLS, network isolation, backups, and service accounts.

The placeholder master-key password must be replaced with a secret generated and stored by the enterprise secret-management system.

### `003_seed_sample_data.sql`

Development/staging sample data only.

Creates:
- roles.
- one operator.
- one sample inspection.
- sample image records.
- sample FOD alert.
- sample operator note.

Do not run this script in production unless adapted for an approved test tenant.

## Documentation Module

```text
docs/
├── CIS-Compliance-Validation.md
└── Deployment-Guide-Windows.md
```

### `CIS-Compliance-Validation.md`

Explains which CIS-aligned controls are implemented in code and which must be validated in the production environment.

Covered areas:
- Windows app controls.
- .NET application-tier controls.
- MSSQL controls.
- required environment validation.

Important: the solution is CIS-aligned, but a formal CIS Level 2 claim requires validation on the deployed Windows hosts, SQL Server, network, identity, certificates, backups, code signing, and monitoring stack.

### `Deployment-Guide-Windows.md`

Enterprise deployment checklist covering:
- prerequisites.
- build command.
- database setup.
- application deployment.
- operational validation.
- audit and backup operations.

## Security Design

The baseline includes the following security controls:

- Role-based access control.
- Viewer export denial.
- Audited report searches.
- Audited exports.
- Audited export denials.
- No plaintext credentials in source.
- Separate application and data tiers.
- SQL least-privilege role scripts.
- SQL audit specification.
- License plate encryption design.
- License plate hash search design.
- Idle session-lock foundation.
- User-safe authorization errors.

Production hardening still needs:
- real identity provider integration.
- signed binaries.
- enterprise secret storage.
- SQL TLS certificates.
- Windows endpoint CIS hardening.
- database backup encryption and restore testing.
- SIEM forwarding.
- DLP/clipboard policy.
- independent CIS validation.

## Build

```powershell
dotnet build VehicleInspectionSuite.sln
```

Expected result:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

## Run

Start the backend API first:

```powershell
dotnet run --project src\VehicleInspection.Api\VehicleInspection.Api.csproj
```

Then start the WPF frontend:

```powershell
dotnet run --project src\VehicleInspection.App\VehicleInspection.App.csproj
```

The WPF app launches the Windows desktop dashboard and starts the frontend device socket listener. External devices connect only to the WPF frontend; the WPF frontend forwards accepted data to the backend API.

## Frontend Device Intake and Backend Persistence

The WPF app listens for newline-delimited JSON device messages on `127.0.0.1:47011` by default. The listener validates and deduplicates UVSS, X-ray, and VLPR device payloads by trigger, then forwards only accepted first-category payloads to the backend API at `http://localhost:5077`.

Accepted categories:
- `Uvss`: undervehicle image plus optional FOD JSON.
- `Xray`: X-ray image.
- `Vlpr`: license plate crop plus license plate number.

First-image-wins rule:
- One `triggerId` maps to one inspection record.
- The first image for each category is accepted.
- Later messages for the same `triggerId` and category are ignored and do not replace the original image, license plate, or FOD data.

Default contract:

```json
{
  "apiKey": "development-key-change-me",
  "triggerId": "TRG-LOCAL-TEST-001",
  "category": "Uvss",
  "timestampUtc": "2026-05-30T12:34:56.789Z",
  "deviceId": "UVSS-DEVICE-01",
  "laneId": "Gate A / Lane 02",
  "imageFormat": "png",
  "imageBase64": "...",
  "licensePlate": null,
  "fodJson": {
    "alerts": [
      {
        "zone": "Rear axle",
        "severity": "High",
        "description": "Foreign object detected near exhaust line",
        "confidence": 0.94
      }
    ]
  }
}
```

Security defaults:
- Devices bind to the frontend listener on localhost for safe local operation.
- Devices do not communicate directly with the backend.
- The frontend requires an API key before forwarding payloads.
- The frontend validates trigger, category, device, lane, image format, and payload size.
- The frontend ignores duplicate categories for the same trigger before forwarding.
- The backend repeats validation defensively and generates server-side filenames under `%LOCALAPPDATA%\\UVSS\\VehicleInspection\\BackendImages`.
- The backend returns image URLs for WPF dashboard/report preview panels.
- Reject unsupported image formats and invalid API keys without crashing the app.

## Current Implementation Notes

- The backend API currently uses an in-memory repository for local development.
- The WPF app reads dashboard/report data from the backend API.
- Image panels display backend image URLs when device payloads provide valid image data, otherwise they show vector placeholders.
- PDF export is represented by a PDF-ready manifest placeholder.
- The SQL scripts define production schema and hardening direction but are not yet wired to a live MSSQL repository.
- The previous `index.html`, `styles.css`, and `script.js` are an earlier web prototype and are not part of the WPF runtime.

## Recommended Next Steps

1. Replace `InMemoryInspectionRepository` with an MSSQL repository.
2. Bind `ZoomPanImageControl` to real image sources from secure storage.
3. Add Windows/domain authentication.
4. Wire `SessionLockService` to global idle tracking and unlock workflow.
5. Add a signed enterprise PDF export library.
6. Add unit and integration tests.
7. Add deployment packaging and code signing.
8. Run CIS benchmark validation in the target Windows and SQL Server environment.
