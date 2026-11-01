param([switch]$SkipWebBuild, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Initialize.ps1')
$taskValues = @{}
foreach ($taskLine in [IO.File]::ReadAllLines((Join-Path $taskRoot '.env'))) {
    if ($taskLine -match '^([A-Z_]+)=(.*)$') { $taskValues[$Matches[1]] = $Matches[2] }
}
$env:Gateway__Bootstrap__AdminApiKey = $taskValues['AUTOBAHN_ADMIN_KEY']
$env:Gateway__Bootstrap__KeyPepper = $taskValues['AUTOBAHN_KEY_PEPPER']
$env:Gateway__Security__KeyPepper = $taskValues['AUTOBAHN_KEY_PEPPER']
$env:Gateway__Metrics__ScrapeToken = $taskValues['AUTOBAHN_METRICS_TOKEN']
$env:ConnectionStrings__GatewayDb = 'Data Source=' + (Join-Path $taskRoot 'data/autobahn.db')
$env:Gateway__ModelsConfigPath = Join-Path $taskRoot 'config/models.json'
$env:Gateway__UpstreamSecretsPath = Join-Path $taskRoot 'data/upstream-secrets.enc'
$env:Gateway__Security__AuditLogPath = Join-Path $taskRoot 'data/audit-log.jsonl'
$env:Fleet__DataDirectory = Join-Path $taskRoot 'data/fleet'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:8080'
if (-not $SkipWebBuild) {
    foreach ($taskWeb in @('Autobahn.Admin.Web', 'Autobahn.Fleet.Web')) {
        Push-Location (Join-Path $taskRoot "src/$taskWeb")
        try {
            npm ci --ignore-scripts
            if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
            npm run build
            if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
        } finally { Pop-Location }
    }
}
if ($NoBuild) {
    dotnet run --project (Join-Path $taskRoot 'src/Autobahn.Host') --no-launch-profile --no-build --no-restore
} else {
    dotnet run --project (Join-Path $taskRoot 'src/Autobahn.Host') --no-launch-profile
}
