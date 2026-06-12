# UVSS Vehicle Inspection Suite — Complete Software Requirements

## 1. SYSTEM OVERVIEW

**UVSS Vehicle Inspection Command** is a Windows desktop application for under-vehicle surveillance system (UVSS) operations. It receives real-time images from vehicle inspection devices (under-vehicle scan, X-ray, license plate camera), displays them with zoom/pan controls, allows operators to review and annotate inspection records, and exports reports.

### 1.1 Architecture
- **3-tier architecture + REST API bridge**
- **Frontend**: WPF (.NET 10, Windows) — the operator console
- **Backend API**: ASP.NET Core Minimal API (.NET 10) — persistence and image storage
- **Domain Layer**: Shared class library — models, services, security, repository interfaces
- **Data Layer**: SQL Server repository (with in-memory fallback for development)

### 1.2 Project Structure
```
src/
  VehicleInspection.App/           # WPF desktop app (WinExe, net10.0-windows)
  VehicleInspection.Api/            # ASP.NET Core Web API (net10.0)
  VehicleInspection.Application/    # Shared domain logic (net10.0)
  VehicleInspection.Data/           # Data access (net10.0)
```

**Dependencies**: App → Application + Data; Api → Application + Data; Data → Application

**NuGet Packages**: QuestPDF 2026.6.0, System.Drawing.Common, Microsoft.Data.SqlClient 7.0.1

---

## 2. BACKEND API (VehicleInspection.Api)

### 2.1 Configuration
- Listens on `UVSS_BACKEND_URL` env var (default: `http://localhost:5077`)
- SQL connection from `UVSS_CONNECTION_STRING` env var (default: `Server=.\\SQLEXPRESS;Database=VehicleInspection;Trusted_Connection=true;TrustServerCertificate=true;`)
- Device API key from `UVSS_DEVICE_API_KEY` env var (default: `development-key-change-me`)
- Image storage: `%LOCALAPPDATA%/UVSS/VehicleInspection/BackendImages/{triggerId}/{filename}`
- Sets QuestPDF Community license on startup

### 2.2 API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/device-ingestion` | Accept device image payload, return record or 202 if duplicate |
| GET | `/api/inspections/current` | Return most recent inspection by ScanTime |
| GET | `/api/inspections?fromDate&toDate&licensePlate&status&fodAlertsOnly` | Search with optional filters |
| GET | `/api/inspections/previous?licensePlate&excludeTriggerId` | Find previous scan for same plate |
| PUT | `/api/inspections/{id}/license-plate` | Update license plate + SHA256 hash |
| PUT | `/api/inspections/{id}/status` | Update inspection status |
| PUT | `/api/inspections/{id}/notes` | Update operator notes |
| GET | `/api/images/{triggerId}/{fileName}` | Serve stored image file |

### 2.3 Device Ingestion Flow
1. Validate API key, field lengths, image format (png/jpg/jpeg/bmp/tif/tiff), image size ≤ 8MB
2. Deduplicate by trigger ID + category (first image per category wins)
3. Decode Base64 image, save to disk as `{category}-{timestamp}.{ext}`
4. Return HTTP URL: `http://localhost:5077/api/images/{triggerId}/{filename}`
5. Apply FOD alerts from `FodJson` payload, normalize severity strings (Critical/High/Medium/Low)

---

## 3. DOMAIN MODELS (VehicleInspection.Application)

### 3.1 InspectionRecord
```
Id: Guid
TriggerId: string
ScanTime: DateTimeOffset
LicensePlate: string (default: "Pending OCR")
LicensePlateHash: string (SHA256 of uppercase plate)
Status: InspectionStatus enum (Pending, Clear, Review, Hold, Escalated)
UnderVehicleImagePath: string (URL or local path)
FullVehicleImagePath: string
LicensePlateImagePath: string
XrayImagePath: string? (nullable)
FodAlerts: IReadOnlyList<FodAlert>
OperatorName: string
Lane: string
Notes: string
SystemHealth: string
SystemErrors: IReadOnlyList<SystemErrorMessage>
Computed: HasXray, HasFodAlerts, HighestFodSeverity
```

### 3.2 FodAlert
```
Id: Guid
Zone: string (e.g., "Rear bumper")
Severity: string (Critical/High/Medium/Low)
Description: string
Confidence: double (0.0-1.0)
```

### 3.3 DeviceIngestionMessage (device payload)
```
ApiKey, TriggerId, Category ("Uvss"/"Xray"/"Vlpr"), TimestampUtc,
DeviceId, LaneId, ImageFormat, ImageBase64, LicensePlate (nullable),
FodJson: FodPayload { Alerts: IReadOnlyList<FodAlertPayload> }
```

### 3.4 ReportFilter
```
FromDate: DateTime?, ToDate: DateTime?
LicensePlate: string
Status: InspectionStatus?
FodAlertsOnly: bool
```

### 3.5 Security Models
- **Role enum**: Viewer, Operator, Admin
- **Permission enum**: ViewDashboard, ViewReports, ExportReports, EditOperatorNotes, ManageConfiguration, ViewAuditLog
- **RBAC mapping**: Viewer→{ViewDashboard,ViewReports}, Operator→{+ExportReports,EditOperatorNotes}, Admin→All
- **UserSession**: UserName, Role, AuthenticationProvider, LoginTime, LastActivityUtc, IsLocked
- **AuditEntry**: EventTimeUtc, UserName, Role, Action, Target, Result, Workstation

### 3.6 Windows Authentication
- Authenticates current Windows user
- If AD configured (`UVSS_AD_DOMAIN`, `UVSS_AD_SERVER` env vars): resolves DC via `DsGetDcName`, matches user groups against AD groups (Administrators/Operators/Viewers)
- Fallback: local Windows group matching
- P/Invokes: Netapi32.dll for `DsGetDcName` and `NetApiBufferFree`

### 3.7 Repository Interface (IInspectionRepository)
```
GetCurrentInspectionAsync() → InspectionRecord
GetInspectionByTriggerIdAsync(triggerId) → InspectionRecord?
UpsertInspectionAsync(record)
SearchAsync(ReportFilter) → IReadOnlyList<InspectionRecord>
GetPreviousByLicensePlateAsync(licensePlate, excludeTriggerId) → InspectionRecord?
UpdateLicensePlateAsync(id, plate, hash)
UpdateInspectionStatusAsync(id, status)
UpdateNotesAsync(id, notes)
AddAuditEntryAsync(entry)
GetAuditEntriesAsync() → IReadOnlyList<AuditEntry>
```

---

## 4. SQL DATABASE (VehicleInspection.Data)

### 4.1 Tables
**Inspections table**: Columns for all InspectionRecord fields. `FodAlerts` and `SystemErrors` stored as JSON in NVARCHAR(MAX). Auto-created on first connection.

**AuditEntries table**: Columns for all AuditEntry fields. Index on EventTimeUtc.

### 4.2 Seed Data (in-memory + SQL)
Three sample records:
1. **SEC-2048**: Status=Review, Lane="Gate A / Lane 02", 2 FOD alerts, 4 system errors
2. **UVS-1186**: Status=Clear, Lane="Gate B / Lane 01", no FOD, has X-ray
3. **GOV-7605**: Status=Escalated, Lane="Gate A / Lane 01", 1 Critical FOD alert

---

## 5. WPF FRONTEND (VehicleInspection.App)

### 5.1 Window Structure
- **WindowStyle**: None (custom chrome)
- **WindowState**: Maximized
- **ResizeMode**: NoResize
- **Topmost**: False

### 5.2 Main Layout (2 rows)
```
┌──────────────────────────────────────────────────────────────────┐
│ TOP BAR (52px) — dark navy background                           │
│ [Brand] [Dashboard btn] [Search btn]    [Socket] [User] [Role] [Lang] [Exit] │
├──────────────────────────────────────────────────────────────────┤
│ CONTENT AREA — swaps between DashboardView and ReportView        │
└──────────────────────────────────────────────────────────────────┘
```

### 5.3 Top Bar Details
- **Left**: Brand logo (UV icon in blue box) + "TeleRadio" / "Vehicle Screening"
- **Center**: Dashboard button + Search button (horizontally centered)
- **Right** (left to right): Socket status text → User display → Role display → Language toggle button → Exit button
- Language toggle cycles: English → Arabic → Malay → Thai → English

### 5.4 Language Switching
- Resource dictionaries: `Strings.en-US.xaml`, `Strings.ar-SA.xaml`, `Strings.ms-MY.xaml`, `Strings.th-TH.xaml`
- Hot-swapped at runtime: old language dictionary removed from `Application.Current.Resources.MergedDictionaries`, new one added
- `Loc.LanguageChanged` event fires, all ViewModels and code-behind re-read strings
- XAML uses `{DynamicResource Key}` for automatic re-evaluation

### 5.5 Exit Button
- Located at far right of top bar, after language toggle
- Shows confirmation MessageBox: "Are you sure you want to exit the application?" (localized)
- Yes = `Application.Current.Shutdown()` (triggers clean shutdown: audit log + socket stop)
- No = cancel

### 5.6 Startup Flow
1. Set QuestPDF Community license
2. Construct all services (HTTP client, repository, auth, inspection, export, socket listener)
3. Authenticate Windows user → determine Role
4. Language switch to English
5. Initialize: record login audit, load dashboard, apply report filters, start TCP socket listener

---

## 6. DASHBOARD VIEW

### 6.1 Layout (2-column)
```
┌──────────────────────────────────────────────┬──────────────────────┐
│ UVSS: CURRENT SCAN           [ROI] [Slider] │ VLPR PLATE IMAGE     │
│ ┌──────────────────────────────────────────┐ │ ┌──────────────────┐ │
│ │   Full image with ROI rectangles         │ │ │  Plate image     │ │
│ │   (ZoomPanImageControl)                  │ │ │  (ZoomPan)       │ │
│ └──────────────────────────────────────────┘ │ └──────────────────┘ │
│                                              │ X-RAY IMAGE          │
│ UVSS: PREVIOUS SCAN                          │ ┌──────────────────┐ │
│ ┌──────────────────────────────────────────┐ │ │  X-ray image     │ │
│ │   Previous scan (same plate)             │ │ │  (ZoomPan)       │ │
│ │   (ZoomPanImageControl, synced zoom)     │ │ └──────────────────┘ │
│ └──────────────────────────────────────────┘ │ OPERATOR NOTES       │
│                                              │ [textbox multiline]  │
│                                              │ FOD: [severity badge]│
│                                              │ Status: [dropdown]   │
│                                              │ System: [errors]     │
│                                              │ Lane: [text] Time    │
└──────────────────────────────────────────────┴──────────────────────┘
```
- Left column (1.87*): Current UVSS (top), Previous UVSS (bottom) — synced zoom/pan
- Right column (1*): VLPR image, X-ray image, Notes textbox, Status dropdown, FOD badge, System errors, Lane + ScanTime

### 6.2 Dashboard Data Flow
- On load: fetch current inspection from API
- On current inspection update (device event): refresh dashboard via `Dispatcher.Invoke`
- License plate update: editable via VLPR image overlay TextBox → SHA256 hash computed
- Notes update: Enter key or LostFocus commits to API
- Previous scan: fetched by license plate (excludes current trigger ID)

---

## 7. SEARCH / REPORTS VIEW

### 7.1 Layout (4 rows)
```
┌──────────────────────────────────────────────────────────────────┐
│ FILTER BAR                                                       │
│ [Date From] [Date To] [Plate] [Status▼] [☐ FOD only]            │
│ [Apply] [Export CSV] [Export PDF] [Export Current Record]        │
├──────────────────────────────────────────────────────────────────┤
│ DATAGRID: ScanTime | Plate | Status | Lane | Operator | FOD | System │
│ (Plate is editable inline with Enter key; row selection triggers detail) │
├──────────────────────────────────────────────────────────────────┤
│ SELECTED RECORD DETAIL                           Trigger: [ID]   │
│ NOTES: [textbox]                                                 │
│ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐              │
│ │ UVSS UNDER   │ │ X-RAY IMAGE  │ │ VLPR IMAGE   │              │
│ │ VEHICLE      │ │              │ │              │              │
│ │ [ROI slider] │ │ (ZoomPan)    │ │ (ZoomPan)    │              │
│ │ (ZoomPan)    │ │              │ │              │              │
│ └──────────────┘ └──────────────┘ └──────────────┘              │
├──────────────────────────────────────────────────────────────────┤
│ STATUS BAR: "{N} records loaded. Export permission: granted"     │
└──────────────────────────────────────────────────────────────────┘
```

### 7.2 Filter Controls
- **FromDate / ToDate**: DatePicker, default: yesterday to today
- **LicensePlate**: TextBox
- **Status**: ComboBox with "All" + translated status options
- **FodAlertsOnly**: CheckBox
- **ApplyFilters**: Button, triggers search
- **Export CSV**: Exports all search results as CSV
- **Export PDF**: Exports all search results as tabular A4 PDF
- **Export Current Record**: Exports single selected record as detailed landscape PDF

### 7.3 DataGrid
- 7 columns: ScanTime, Plate (editable TextBox), Status, Lane, Operator, FOD count, System
- Alternating row colors
- SelectedItem bound to `SelectedRecord` (TwoWay)
- Column headers localized — refresh on language change

### 7.4 Detail Panel (below DataGrid)
- Shows selected record's images in `UniformGrid` (3 columns)
- **UVSS panel**: ROI slider (1-5) with sensitivity level display. ROI overlay from `D:\image\transaction\roi1.json`
- **X-ray panel**: ZoomPanImageControl
- **VLPR panel**: ZoomPanImageControl
- Notes textbox below images (Enter/LostFocus commits)
- Trigger ID shown in header

### 7.5 Inline Editing
- **Plate**: Enter key in DataGrid TextBox → calls `UpdateLicensePlateAsync()` → recomputes SHA256
- **Notes**: Enter/LostFocus in detail panel → calls `UpdateNotesAsync()`
- **Status**: ComboBox SelectionChanged → calls `UpdateInspectionStatusAsync()`

### 7.6 Security Message Bar
- Shows count of loaded records + export permission status
- Shows results of update operations (success/failure)
- Shows CSV/PDF export file paths

---

## 8. EXPORT SERVICE

### 8.1 CSV Export
- Header: `ScanTime,LicensePlate,Status,Lane,Operator,FodAlerts,SystemHealth`
- CSV escaping for commas, quotes, newlines
- Output: `Documents/uvss-report-{yyyyMMdd-HHmmss}.csv`
- RBAC gated: requires `Permission.ExportReports`

### 8.2 PDF Export (All Records)
- A4 portrait
- Table: ScanTime, Plate, Status, Lane, Operator, FOD
- Blue header row, alternating row colors
- Footer: "UVSS Secure Inspection Report — Confidential"

### 8.3 Single Record PDF Export (Current Record)
- **Page**: A4 landscape, single page, 16mm margins, vertically centered content
- **Header**:
  - Title: "UVSS Vehicle Inspection Report — Single Record" (18pt, bold, blue)
  - Timestamp: right-aligned (9pt)
  - Metadata row: Plate, Lane, Operator, Status (12pt, black, labels bold)
  - Blue horizontal separator line
- **Content (65/35 vertical split)**:
  - **Top (65%, ~260pt)**: UVSS under-vehicle image with ROI rectangles composited
    - ROI boxes loaded from `D:\image\transaction\roi1.json`
    - Coordinates mapped from 8192×4096 source space to actual image dimensions
    - Drawn as semi-transparent filled rectangles with colored borders:
      - L1 = Red, L2 = Orange, L3 = Yellow, L4 = Green, L5 = Blue
    - Image resized to max 2000px before compositing using System.Drawing
    - Missing image: plain black background
  - **Separator**: Blue horizontal line with padding
  - **Bottom (35%, ~150pt)**: 3-column horizontal row:
    - **Left 40%**: VLPR license plate image with label
    - **Middle 40%**: X-ray image with label
    - **Right 20%**: "SCAN INFO" label (blue, matching X-ray label) + bordered box containing:
      - Scan time + lane
      - "NOTES" section (if notes exist)
    - Missing images: plain black background
    - All 3 columns aligned at same height via `ExtendVertical()`
- **Footer**: "UVSS Secure Inspection Report — Single Record — Confidential"
- **Sensitivity level**: configurable (1-5), passed from UI slider
- **RBAC gated**: requires `Permission.ExportReports`
- **Audit logged**: both success and denial

---

## 9. ZOOMPANIMAGECONTROL (Custom WPF Control)

### 9.1 Features
- Image display with zoom (0.5x–5x via mouse wheel) and pan (left mouse drag)
- Two controls can be synced via `SyncTarget` property
- Right mouse drag: right 25% adjusts contrast (0.3–2.0), left 25% adjusts brightness (-0.3 to +0.3)
- Pixel-level contrast/brightness via `Parallel.For` on BGRA32 pixel buffer
- ROI overlay: loads JSON from `D:\image\transaction\roi1.json`, draws colored `Border` rectangles on Canvas, sensitivity level filters which boxes are shown
- License plate overlay: editable TextBox (Enter commits, Escape reverts, LostFocus commits)
- Placeholder: SVG-like vehicle diagram shown when no image (configurable text)
- **No-image behavior**: all diagram shapes hidden, black background with placeholder text only
- Reset button and zoom badge in toolbar
- Canvas scales proportionally to control size

### 9.2 Dependency Properties
| Property | Type | Default | Description |
|----------|------|---------|-------------|
| PlaceholderText | string | "Inspection image" | Text shown when no image loaded |
| ImagePath | string | "" | URL or file path to image |
| FodRegions | ObservableCollection\<FodRegion\> | null | FOD overlay regions |
| SensitivityLevel | int | 3 | ROI filter level (1-5) |
| ShowRoiOverlay | bool | false | Enable ROI rectangle overlay |
| SyncTarget | ZoomPanImageControl | null | Linked control for synchronized zoom/pan |
| LicensePlateOverlay | string | "" | Text for license plate badge overlay |

### 9.3 FodRegion Model
```
X: double, Y: double, Width: double, Height: double
Label: string, SeverityLevel: string
FillBrush: Brush, StrokeBrush: Brush
```

### 9.4 ROI JSON Format
File at `D:\image\transaction\roi1.json`:
```json
{
  "L1": { "0": {"x": 2047, "y": 608, "w": 768, "h": 384}, ... },
  "L2": { ... },
  "L3": { ... },
  "L4": { ... },
  "L5": { ... }
}
```
- Top-level keys: "L1" through "L5" (sensitivity levels)
- Inner keys: numeric zone IDs
- Coordinates in 8192×4096 source image space
- Mapped to panel dimensions maintaining aspect ratio (letterboxed)

---

## 10. SERVICES (Frontend)

### 10.1 BackendInspectionClient
- HTTP client wrapping all API endpoints
- Base URL from `UVSS_BACKEND_URL` env var (default: `http://localhost:5077`)
- All methods are async with CancellationToken

### 10.2 TCPDeviceSocketListener
- Binds to configurable IP/port (default: 127.0.0.1:47011)
- Accepts TCP connections, reads newline-delimited UTF-8 JSON
- 10MB max message size per line (`ReadBoundedLineAsync`)
- Each line passed to `FrontendDeviceIngestionForwarder.ProcessJsonAsync()`
- Status updates via `StatusChanged` event

### 10.3 FrontendDeviceIngestionForwarder
- Validates API key, field lengths, image format, image size (≤8MB)
- Frontend deduplication: ConcurrentDictionary tracks received categories per trigger
- Forwards accepted messages to backend API via `BackendInspectionClient`
- Raises `InspectionUpdated` event on successful forward
- Raises `MessageIgnored` event on duplicate

### 10.4 InspectionService
- Wraps repository operations with audit logging
- Each operation records: action name, target, result
- License plate updates include SHA256 hashing

### 10.5 AuditService
- Creates `AuditEntry` records
- Writes through `IInspectionRepository.AddAuditEntryAsync()`

### 10.6 SessionLockService
- `ShouldLock()` returns true if idle for 15+ minutes

---

## 11. DARK THEME DESIGN SYSTEM

### 11.1 Color Palette
| Name | Color | Usage |
|------|-------|-------|
| UvssNavyBrush | #1A242F | Top bar background |
| UvssDeepBrush | #101820 | Main background |
| UvssPanelBrush | #162230 | Panel backgrounds |
| UvssBlueBrush | #1F6FBF | Brand elements |
| UvssBlueBrightBrush | #4EA3E6 | Accents, highlights |
| UvssDangerBrush | #E35D5B | Error/danger |
| UvssWarningBrush | #E7B84D | Warnings |
| UvssTextBrush | #D4E2F0 | Primary text |
| MutedText | #7B93A8 | Secondary text |

### 11.2 Styled Controls
- **Button**: Dark blue (#165A9F), light blue border, hover (#2182D8), pressed (#0B3C6E)
- **PrimaryButton**: MinHeight=38, Padding=16,8
- **TextBox/ComboBox**: Dark backgrounds with light borders
- **DataGrid**: Dark headers, alternating row colors, blue selection
- **DatePicker**: Dark calendar popup

---

## 12. LOCALIZATION

### 12.1 Supported Languages
- English (en-US) — default
- Arabic (ar-SA) — RTL
- Malay (ms-MY)
- Thai (th-TH)

### 12.2 String Resources (~95 keys)
Key groups:
- **App identity**: AppTitle, BrandName, BrandSubtitle
- **Navigation**: Dashboard, Reports, Language, Exit, ExitConfirm
- **Dashboard**: UvssCurrentScan, UvssPreviousScan, VlprPlateImage, XrayImage, OperatorNotesLabel, FodLabel, NoFod, AllSubsystemsOperational, etc.
- **Search**: FilterReports, DateFrom, DateTo, LicensePlate, Status, FodOnly, ApplyFilters, ExportCsv, ExportPdf, ExportCurrentRecord, SelectedRecord, etc.
- **Table headers**: ScanTime, Plate, Lane, Operator, FodHeader, SystemHeader, Trigger
- **Status values**: StatusPending, StatusClear, StatusReview, StatusHold, StatusEscalated
- **FOD severity**: FodSeverityClear, FodSeverityCritical, FodSeverityHigh, FodSeverityMedium, FodSeverityLow
- **Sensitivity**: SensitivityL1 through SensitivityL5
- **Messages**: SocketStopped, StatusAll, Reset, RecordsLoaded, LicensePlateUpdatedMsg, UpdateFailed, StatusUpdatedMsg

### 12.3 Dynamic Resource Pattern
- XAML: `Content="{DynamicResource Key}"`
- Code: `Loc.Get("Key")` or `Loc.Format("Key", args...)`
- Language change: swap ResourceDictionary, fire `Loc.NotifyLanguageChanged()`

---

## 13. CONFIGURATION (Environment Variables)

| Variable | Default | Description |
|----------|---------|-------------|
| UVSS_BACKEND_URL | http://localhost:5077 | API base URL |
| UVSS_CONNECTION_STRING | Server=.\\SQLEXPRESS;Database=VehicleInspection;Trusted_Connection=true;TrustServerCertificate=true; | SQL connection |
| UVSS_DEVICE_API_KEY | development-key-change-me | Device authentication key |
| UVSS_AD_DOMAIN | (optional) | Active Directory domain |
| UVSS_AD_SERVER | (optional) | Active Directory server |

---

## 14. FILE SYSTEM

| Path | Purpose |
|------|---------|
| %LOCALAPPDATA%/UVSS/VehicleInspection/BackendImages/ | API image storage |
| D:\image\transaction\roi1.json | ROI box definitions |
| Documents/uvss-report-{timestamp}.csv | CSV exports |
| Documents/uvss-report-{timestamp}.pdf | PDF bulk exports |
| Documents/uvss-single-{plate}-{timestamp}.pdf | Single record PDF exports |

---

## 15. KEY BEHAVIORS

### 15.1 Image Deduplication
- Frontend (WPF): ConcurrentDictionary per trigger ID tracks received categories
- Backend (API): Same pattern via IngestionRecordState
- First image per category per trigger wins; duplicates raise "ignored" events

### 15.2 Audit Trail
- Every action logged: login, logout, view dashboard, search, export, update plate/status/notes
- Authorization checks also audited (AccessGranted/AccessDenied)
- All entries include: timestamp, username, role, action, target, result, workstation

### 15.3 RBAC Enforcement
- UI level: Command `CanExecute` guards check `AccessControlService.Can(role, permission)`
- Service level: `AuditedAuthorizationService.AuthorizeAsync()` throws `UnauthorizedAccessException`
- Export: both CSV and PDF gated by `Permission.ExportReports`

### 15.4 Error Handling
- ViewModel try/catch blocks set `SecurityMessage` for display
- Authorization failures show MessageBox warning
- Network/download failures: graceful degradation (null returns, placeholder UI)
- Image compositing failures: falls back to original image

### 15.5 No-Image Display
- All image panels show **black background** when no image is available
- Placeholder **text** is displayed (e.g., "Selected record UVSS image", "No previous scan")
- All diagram/pattern shapes are hidden
- ROI slider and controls are hidden when no UVSS image

---

## 16. UI INTERACTION REFERENCE

| Action | Trigger | Result |
|--------|---------|--------|
| Switch view | Click Dashboard/Search button | Content area swaps view |
| Change language | Click language button | All text updates immediately |
| Zoom | Mouse wheel on image | 0.5x–5x zoom |
| Pan | Left-drag on image | Image pans |
| Adjust contrast | Right-drag on right 25% | Contrast 0.3–2.0 |
| Adjust brightness | Right-drag on left 25% | Brightness -0.3 to +0.3 |
| Reset view | Click Reset button | Zoom=1x, centered |
| Edit plate | Click plate text overlay | Type new plate, Enter to save |
| Edit notes | Type in notes textbox | Enter or click away to save |
| Edit status | Select from dropdown | Immediately saves |
| Search | Set filters + click Apply | DataGrid refreshes |
| Select record | Click DataGrid row | Detail panel updates |
| Export CSV | Click Export CSV | All filtered records → CSV file |
| Export PDF | Click Export PDF | All filtered records → PDF file |
| Export Current | Click Export Current Record | Selected record → landscape PDF with ROI |
| Adjust ROI | Drag ROI slider (1-5) | ROI rectangles filter by level |
| Exit app | Click Exit button | Confirmation dialog → shutdown |
