# Run from any directory with PowerShell 7 and Docker Compose.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$project = 'nabra-test-' + [Guid]::NewGuid().ToString('N')
$oldResults = $env:NABRA_RESULTS
$folder = (New-Item -ItemType Directory -Path (Join-Path $root ".quality/$project") -Force).FullName
$env:NABRA_RESULTS = $folder.Replace('\','/')
$compose = @('compose','-p',$project,'-f',(Join-Path $root 'compose.test.yml'))
try {
    docker @compose run --rm tests
    if ($LASTEXITCODE -ne 0) { throw 'Backend build or tests failed.' }
    $reports = @(Get-ChildItem $folder -Recurse -Filter 'coverage.cobertura.xml')
    if ($reports.Count -ne 4) { throw "Expected four test project coverage reports, found $($reports.Count)." }
    $lines = @{}
    foreach ($report in $reports) {
        [xml]$coverage = Get-Content -LiteralPath $report.FullName -Raw
        foreach ($package in $coverage.coverage.packages.package) {
            if ($package.name -notin @('OmniAI.Domain','OmniAI.Application','OmniAI.Infrastructure','OmniAI.Api')) { continue }
            foreach ($class in $package.classes.class) {
                foreach ($line in $class.lines.line) {
                    $key = "$($package.name)/$($class.filename):$($line.number)"
                    $lines[$key] = ($lines[$key] -or [int]$line.hits -gt 0)
                }
            }
        }
    }
    if ($lines.Count -eq 0) { throw 'No production source lines measured.' }
    $percent = 100 * @($lines.Values.Where({ $_ })).Count / $lines.Count
    Write-Host ('Backend line coverage {0:N2}% over {1} lines. Reports in {2}' -f $percent,$lines.Count,$folder)
    if ($percent -lt 75) { throw 'Backend line coverage below 75%.' }
} finally {
    docker @compose down --volumes
    $cleanup = $LASTEXITCODE
    $env:NABRA_RESULTS = $oldResults
    if ($cleanup -ne 0) { throw "Could not clean Docker project $project." }
}
