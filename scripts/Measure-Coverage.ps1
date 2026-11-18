param(
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts/coverage',
    [switch]$NoBuild
)
# Runs every test project with coverlet, merges the Cobertura files with ReportGenerator
# and prints the per-assembly summary. Output: $Output/raw (Cobertura per test project),
# $Output/report (HTML, Summary.txt, SummaryGithub.md, merged Cobertura.xml).
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    $raw = Join-Path $Output 'raw'
    $report = Join-Path $Output 'report'
    foreach ($directory in @($raw, $report)) {
        if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory -Recurse -Force }
    }
    $arguments = @('test', 'Trag.sln', '-c', $Configuration, '--collect', 'XPlat Code Coverage',
        '--settings', 'coverage.runsettings', '--results-directory', $raw)
    if ($NoBuild) { $arguments += '--no-build' }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed ($LASTEXITCODE)." }

    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed ($LASTEXITCODE)." }
    $reports = Get-ChildItem -LiteralPath $raw -Recurse -Filter 'coverage.cobertura.xml' | ForEach-Object { $_.FullName }
    if ($reports.Count -eq 0) { throw 'No coverage.cobertura.xml was produced.' }
    & dotnet reportgenerator "-reports:$($reports -join ';')" "-targetdir:$report" `
        '-reporttypes:Html;TextSummary;MarkdownSummaryGithub;Cobertura' '-assemblyfilters:+Trag.*' '-verbosity:Warning'
    if ($LASTEXITCODE -ne 0) { throw "reportgenerator failed ($LASTEXITCODE)." }
    Get-Content -LiteralPath (Join-Path $report 'Summary.txt')
}
finally {
    Pop-Location
}
