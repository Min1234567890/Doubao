$Suffix = if ($args.Count -ge 1) { $args[0] } else { "3" }
$Plate  = if ($args.Count -ge 2) { $args[1] } else { "GBE6888E" }

$triggerId = "TRIGGER-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$Suffix"
$apiKey = "development-key-change-me"
$deviceId = "DEVICE-SIM-001"
$laneId = "LANE-1"
$baseDir = Join-Path $PSScriptRoot "transaction"

$uvssFile = "uvss${Suffix}.jpg"
$xrayFile = "xray${Suffix}.jpg"
$vlprFile = "lp${Suffix}.jpg"
$roiFile  = "roi${Suffix}.json"

Write-Host "Trigger ID : $triggerId" -ForegroundColor Cyan
Write-Host "Suffix     : $Suffix" -ForegroundColor Cyan
Write-Host "Plate      : $Plate" -ForegroundColor Cyan
Write-Host "Files      : $uvssFile  $xrayFile  $vlprFile  $roiFile" -ForegroundColor DarkGray
Write-Host ""

# ── Helpers ──
function Get-Base64($path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    return [System.Convert]::ToBase64String($bytes)
}

function Get-FodPayload {
    $roiPath = Join-Path $baseDir $roiFile
    if (-not (Test-Path $roiPath)) {
        Write-Host "ROI file not found: $roiFile -- skipping FOD" -ForegroundColor DarkYellow
        return $null
    }
    $roi = Get-Content $roiPath -Raw | ConvertFrom-Json
    $alerts = @()
    $severityMap = @{
        "L1" = @{ Name = "Critical"; Conf = 0.95 }
        "L2" = @{ Name = "High";     Conf = 0.90 }
        "L3" = @{ Name = "Medium";   Conf = 0.80 }
        "L4" = @{ Name = "Low";      Conf = 0.65 }
        "L5" = @{ Name = "Info";     Conf = 0.50 }
    }
    foreach ($level in $roi.PSObject.Properties) {
        $sev = $severityMap[$level.Name]
        if (-not $sev) { continue }
        foreach ($classId in $level.Value.PSObject.Properties) {
            $box = $classId.Value
            $alerts += @{
                Zone        = "$($level.Name)-$($classId.Name)"
                Severity    = $sev.Name
                Description = "ROI zone $($classId.Name) at [$($box.x),$($box.y) $($box.w)x$($box.h)]"
                Confidence  = $sev.Conf
            }
        }
    }
    return @{ alerts = $alerts }
}

function Send-Message($category, $imageFile, $licensePlate, $fodObj) {
    $imgPath = Join-Path $baseDir $imageFile
    if (-not (Test-Path $imgPath)) {
        Write-Host "  MISSING: $imageFile -- skipped" -ForegroundColor Red
        return
    }
    $b64 = Get-Base64 $imgPath
    $ext = [System.IO.Path]::GetExtension($imageFile).TrimStart('.')

    $msg = @{
        ApiKey       = $apiKey
        TriggerId    = $triggerId
        Category     = $category
        TimestampUtc = [DateTime]::UtcNow.ToString("o")
        DeviceId     = $deviceId
        LaneId       = $laneId
        ImageFormat  = $ext
        ImageBase64  = $b64
    }
    if ($licensePlate) { $msg.LicensePlate = $licensePlate }
    if ($fodObj)       { $msg.FodJson       = $fodObj }

    $json = $msg | ConvertTo-Json -Depth 10 -Compress
    Write-Host "Sending $category ($([math]::Round($json.Length/1024,1)) KB)..." -ForegroundColor Yellow

    try {
        $client = New-Object System.Net.Sockets.TcpClient("127.0.0.1", 47011)
        $stream = $client.GetStream()
        $writer = New-Object System.IO.StreamWriter($stream)
        $writer.AutoFlush = $true
        $writer.WriteLine($json)
        $writer.Close()
        $stream.Close()
        $client.Close()
        Write-Host "  -> $category OK" -ForegroundColor Green
    } catch {
        Write-Host "  -> $category FAILED: $_" -ForegroundColor Red
    }
}

# ── Main ──
$fod = Get-FodPayload
if ($fod) {
    Write-Host "FOD zones   : $($fod.alerts.Count)" -ForegroundColor Magenta
} else {
    Write-Host "FOD zones   : 0" -ForegroundColor DarkYellow
}
Write-Host ""

Send-Message "Uvss" $uvssFile $null  $fod
Start-Sleep -Milliseconds 500
Send-Message "Xray" $xrayFile $null  $null
Start-Sleep -Milliseconds 500
Send-Message "Vlpr" $vlprFile $Plate $null

Write-Host ""
Write-Host "Done. Trigger $triggerId complete." -ForegroundColor Cyan
