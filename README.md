# UVSS Vehicle Inspection Suite

Windows-native enterprise dashboard for vehicle security screening operations. Built in C# with WPF (.NET 10) and ASP.NET Core Minimal API, following a 3-tier architecture for presentation, application logic, and data persistence.

The system is designed for 24/7 security operation centers where the under-vehicle image is the primary inspection artifact. It supports under-vehicle scan review, license plate OCR context, X-ray imagery, ROI (Region of Interest) overlay with 5-level sensitivity, FOD alert review, 4-language UI (English, Arabic, Malay, Thai), report filtering, CSV/PDF export workflows, single-record landscape PDF export with composited ROI rectangles, RBAC, audit logging, Windows-integrated authentication, and MSSQL persistence.

## Solution Overview

```text
VehicleInspectionSuite.sln
├── src/
│   ├── VehicleInspection.App/          # WPF Windows desktop presentation tier
│   ├── VehicleInspection.Api/          # ASP.NET Core Minimal API backend
│   ├── VehicleInspection.Application/  # Business logic, models, RBAC, audit, export services
│   └── VehicleInspection.Data/         # Repository implementations (InMemory + MSSQL)
├── database/                           # MSSQL schema, hardening, seed scripts
├── docs/                               # CIS validation and Windows deployment guides
├── UVSS_SOFTWARE_REQUIREMENTS.md       # Complete architecture and GUI requirements spec
├── index.html                          # Earlier UVSS web prototype, not used by WPF app
├── styles.css                          # Earlier UVSS web prototype, not used by WPF app
└── script.js                           # Earlier UVSS web prototype, not used by WPF app
```

## Architecture

The application follows a 3-tier enterprise architecture with a REST API bridge.

```
Devices (UVSS/X-ray/VLPR)
    |  TCP JSON (newline-delimited, base64 images)
    v
WPF App (VehicleInspection.App)
    |-- TcpDeviceSocketListener (127.0.0.1:47011)
    |-- FrontendDeviceIngestionForwarder (validate + deduplicate)
    |-- BackendInspectionClient (HTTP to API)
    |
    v
ASP.NET API (VehicleInspection.Api)
    |-- Minimal API endpoints (8 routes)
    |-- DeviceIngestionService (backend validation, image persistence)
    |-- IInspectionRepository → SqlInspectionRepository (MSSQL)
```

### 1. Presentation Tier: `VehicleInspection.App`

WPF desktop application targeting `net10.0-windows`.

**Key modules:**

```text
src/VehicleInspection.App/
├── App.xaml / App.xaml.cs
├── MainWindow.xaml / MainWindow.xaml.cs
├── Controls/
│   ├── ZoomPanImageControl.xaml
│   └── ZoomPanImageControl.xaml.cs
├── Localization/
│   ├── Loc.cs
│   └── StatusDisplayConverter.cs
├── Resources/
│   ├── Theme.xaml
│   ├── Strings.en-US.xaml
│   ├── Strings.ar-SA.xaml
│   ├── Strings.ms-MY.xaml
│   └── Strings.th-TH.xaml
├── Services/
│   ├── BackendInspectionClient.cs
│   ├── FrontendDeviceIngestionForwarder.cs
│   ├── HttpInspectionRepository.cs
│   └── TcpDeviceSocketListener.cs
├── ViewModels/
│   ├── DashboardViewModel.cs
│   ├── MainViewModel.cs
│   ├── RelayCommand.cs
│   ├── ReportViewModel.cs
│   └── ViewModelBase.cs
└── Views/
    ├── DashboardView.xaml / .xaml.cs
    └── ReportView.xaml / .xaml.cs
```

#### MainWindow

The main shell contains:
- Top bar with UVSS branding, nav buttons (Dashboard/Search), socket status, user, role, language toggle, and Exit button with confirmation dialog.
- Content area that swaps between Dashboard and Reports views.
- Runtime resource dictionary replacement for 4-language switching.

#### DashboardView

The operations dashboard arranged around the inspection workflow:
- **Left column**: Current UVSS scan (with ROI overlay + sensitivity slider), Previous UVSS scan (synced zoom/pan).
- **Right column**: VLPR plate image (with editable license plate overlay), X-ray image, operator notes textbox, FOD severity badge, status ComboBox, system error summary, lane and scan time.
- No-image display: black background with placeholder text (no diagram pattern).

#### ReportView

Dedicated search/report page for inspection history:
- **Filter bar**: Date From/To pickers, License Plate textbox, Status ComboBox, FOD-only checkbox, Apply Filters button.
- **Export buttons**: Export CSV, Export PDF (all records), Export Current Record (single record with ROI).
- **DataGrid**: ScanTime, Plate (editable inline), Status, Lane, Operator, FOD count, System.
- **Detail panel**: Selected record images (UVSS with ROI slider, X-ray, VLPR), notes textbox.
- RBAC-gated export commands with audit logging.
- Inline editing: plate (Enter key), notes (Enter/LostFocus), status (ComboBox selection).
- No-image display: black background with placeholder text, ROI controls hidden.

#### ZoomPanImageControl

Reusable WPF control for all inspection image panels:
- Mouse wheel zoom (0.5x–5x), left-drag pan, synchronized across linked controls.
- Right-drag contrast/brightness adjustment per-pixel via `Parallel.For` on BGRA32.
- ROI overlay: loads `D:\image\transaction\roi1.json`, draws colored rectangles scaled from 8192×4096 source to panel, filtered by 5-level sensitivity.
- License plate overlay: editable TextBox (Enter commits, Escape reverts).
- No-image display: black background with placeholder text, all diagram shapes hidden.

#### Localization

4-language support with hot-swappable resource dictionaries:
- English (en-US) — default
- Arabic (ar-SA)
- Malay (ms-MY)
- Thai (th-TH)

Uses `{DynamicResource}` in XAML and `Loc.Get()` / `Loc.Format()` in C#. ~95 string keys covering all UI text.

#### Theme.xaml

Dark theme design system optimized for security operations rooms. Colors: deep navy (#101820), navy/charcoal (#1A242F), corporate blue (#1F6FBF), bright blue (#4EA3E6). Custom styles for Button (hover/pressed/disabled), TextBox, ComboBox, DataGrid, DatePicker, Calendar.

### 2. API Tier: `VehicleInspection.Api`

ASP.NET Core Minimal API targeting `net10.0`.

**Endpoints:**

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/device-ingestion` | Accept device payload, return record or 202 |
| GET | `/api/inspections/current` | Most recent inspection |
| GET | `/api/inspections` | Search with optional date/plate/status/FOD filters |
| GET | `/api/inspections/previous` | Previous scan by license plate |
| PUT | `/api/inspections/{id}/license-plate` | Update plate + hash |
| PUT | `/api/inspections/{id}/status` | Update status |
| PUT | `/api/inspections/{id}/notes` | Update notes |
| GET | `/api/images/{triggerId}/{fileName}` | Serve stored image |

Image storage: `%LOCALAPPDATA%/UVSS/VehicleInspection/BackendImages/{triggerId}/`.

### 3. Application Tier: `VehicleInspection.Application`

Class library targeting `net10.0`.

**Models**: InspectionRecord, FodAlert, DeviceIngestionMessage, ReportFilter, AuditEntry, UserSession, SystemErrorMessage, IngestionRecordState.

**Services**:
- `InspectionService` — orchestrates inspection operations with audit logging.
- `AuditService` — creates and persists audit entries.
- `ExportService` — CSV export, PDF bulk export (tabular), PDF single-record export (landscape A4 with composited ROI rectangles using QuestPDF + System.Drawing).
- `DeviceIngestionService` — validates, deduplicates, persists incoming device images.
- `SessionLockService` — 15-minute idle lock.

**Security**:
- Roles: Viewer, Operator, Admin.
- Permissions: ViewDashboard, ViewReports, ExportReports, EditOperatorNotes, ManageConfiguration, ViewAuditLog.
- `AccessControlService` — maps roles to permissions.
- `AuditedAuthorizationService` — wraps authorization with audit logging.
- `WindowsAuthenticationService` — authenticates current Windows user via Active Directory (with local group fallback).

### 4. Data Tier: `VehicleInspection.Data`

Class library targeting `net10.0`.

**Repositories**:
- `InMemoryInspectionRepository` — thread-safe in-memory implementation for development with seed data (3 sample inspections).
- `SqlInspectionRepository` — full MSSQL implementation with auto-created schema, parameterized queries, JSON serialization for FOD/system errors, and seed data.

## Export Features

### CSV Export
Exports all filtered records with columns: ScanTime, LicensePlate, Status, Lane, Operator, FodAlerts, SystemHealth.

### PDF Export (All Records)
A4 portrait tabular report of all filtered records.

### Single Record PDF Export
Landscape A4 single-page report for the selected record:
- **Header**: Title, timestamp, Plate/Lane/Operator/Status metadata.
- **Top (65%)**: UVSS image with ROI rectangles composited (color-coded by sensitivity level L1–L5).
- **Bottom (35%)**: VLPR image (40%), X-ray image (40%), Scan Info + Notes (20%).
- ROI sourced from `D:\image\transaction\roi1.json`, mapped from 8192×4096 space.
- Sensitivity level configurable via UI slider (1–5).

## Security Design

- Role-based access control (Viewer/Operator/Admin).
- Audited authorization checks (every permission check recorded).
- Export RBAC gating (Viewers cannot export).
- Windows-integrated authentication with Active Directory support.
- Frontend + backend dual device validation and deduplication.
- First-image-wins rule per trigger per category.
- API key validation for device ingestion.
- License plate SHA256 hashing.
- License plate encryption design (MSSQL AES-256).
- SQL least-privilege role scripts.
- Idle session-lock (15 minutes).

## Configuration

| Variable | Default | Description |
|----------|---------|-------------|
| `UVSS_BACKEND_URL` | `http://localhost:5077` | API base URL |
| `UVSS_CONNECTION_STRING` | `Server=.\\SQLEXPRESS;Database=VehicleInspection;Trusted_Connection=true;...` | SQL connection |
| `UVSS_DEVICE_API_KEY` | `development-key-change-me` | Device auth key |
| `UVSS_AD_DOMAIN` | (optional) | Active Directory domain |
| `UVSS_AD_SERVER` | (optional) | Active Directory server |

## Build

```powershell
dotnet build VehicleInspectionSuite.sln
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

## Run

Start the backend API first:

```powershell
dotnet run --project src\VehicleInspection.Api\VehicleInspection.Api.csproj
```

Then start the WPF frontend:

```powershell
dotnet run --project src\VehicleInspection.App\VehicleInspection.App.csproj
```

The WPF app launches the desktop dashboard and starts the frontend device socket listener on `127.0.0.1:47011`.

## Device Ingestion Contract

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

Accepted categories: `Uvss`, `Xray`, `Vlpr`. First-image-wins: one triggerId → one inspection record. Later messages for the same triggerId + category are ignored.

## Documentation

- `UVSS_SOFTWARE_REQUIREMENTS.md` — complete architecture and GUI requirements specification (can be used to rebuild the application).
- `docs/CIS-Compliance-Validation.md` — CIS-aligned controls documentation.
- `docs/Deployment-Guide-Windows.md` — enterprise deployment checklist.
- `database/` — MSSQL schema, hardening, and seed scripts.
