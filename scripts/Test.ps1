$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
foreach ($taskWeb in @('Autobahn.Admin.Web', 'Autobahn.Fleet.Web')) {
    & npm ci --ignore-scripts --prefix (Join-Path $taskRoot "src/$taskWeb")
    if ($LASTEXITCODE -ne 0) { throw "Frontend dependency installation failed for $taskWeb." }
}
foreach ($taskWeb in @('Autobahn.Admin.Web', 'Autobahn.Fleet.Web')) {
    Push-Location (Join-Path $taskRoot "src/$taskWeb")
    try {
        foreach ($taskArgs in @(@('run','typecheck'),@('test'),@('audit','--audit-level=low'),@('run','build'))) {
            & npm @taskArgs
            if ($LASTEXITCODE -ne 0) { throw "Frontend verification failed for $taskWeb." }
        }
    } finally { Pop-Location }
}
Push-Location $taskRoot
try {
    dotnet build Autobahn.sln
    if ($LASTEXITCODE -ne 0) { throw 'The solution build failed.' }
    dotnet run --project tools/Autobahn.CodeStyle -- .
    if ($LASTEXITCODE -ne 0) { throw 'The C# type/file layout check failed.' }
    dotnet test Autobahn.sln --no-build --logger trx --results-directory artifacts/test-results
    if ($LASTEXITCODE -ne 0) { throw 'The .NET tests failed.' }
} finally { Pop-Location }
