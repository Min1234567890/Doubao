# Volcengine ModelArk (ark- API key) + Claude Code Auto Setup
Write-Host "=== Claude Code + Volcengine ModelArk (ark-) Setup ===" -ForegroundColor Cyan

# Prompt for your ARK API Key
$apiKey = Read-Host -Prompt "ark-c22e22d8-a47a-a2"

# Simple validation
if (-not $apiKey.StartsWith("ark-")) {
    Write-Error "Invalid key! Must start with 'ark-'"
    exit 1
}

# Create .claude folder
$configPath = "$env:USERPROFILE\.claude"
if (-not (Test-Path $configPath)) {
    New-Item -ItemType Directory -Path $configPath -Force | Out-Null
    Write-Host "✅ Created config folder: $configPath" -ForegroundColor Green
}

# Write settings.json for ModelArk Coding Lite
$settings = @{
    env = @{
        ANTHROPIC_AUTH_TOKEN = $apiKey
        ANTHROPIC_BASE_URL   = "https://ark.cn-beijing.volces.com/api/coding"
        ANTHROPIC_MODEL      = "ark-code-latest"
        API_TIMEOUT_MS       = "500000"
    }
} | ConvertTo-Json -Depth 10

$settingsFile = Join-Path $configPath "settings.json"
Set-Content -Path $settingsFile -Value $settings -Encoding UTF8
Write-Host "✅ Config written: $settingsFile" -ForegroundColor Green

Write-Host "`n=== DONE! ===" -ForegroundColor Green
Write-Host "Now run: claude" -ForegroundColor Yellow
Write-Host "Using Volcengine Coding Lite (ark-code-latest)`n" -ForegroundColor Cyan
