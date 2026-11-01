$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
dotnet run --project (Join-Path $taskRoot 'tools/Autobahn.Fleet.Cli') -- init --directory $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Autobahn initialization failed.' }
