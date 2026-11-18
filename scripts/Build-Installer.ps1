param(
    # The version stamped on Trag.exe and on the setup; without one it is the version in Directory.Build.props.
    [string]$Version = ''
)
# Builds the Windows installer locally: the NativeAOT publish into artifacts/publish/win-x64, then Inno Setup packs it
# into dist/Trag-<version>-win-x64-setup.exe. Needs the Visual Studio C++ Build Tools and Inno Setup 6.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (!$Version) { $Version = ([xml](Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }

# Inno Setup installs into Program Files for all users, or into the user's own programs for one.
$compiler = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (!$compiler) { throw 'Inno Setup 6 was not found.' }

& (Join-Path $PSScriptRoot 'Publish-Windows.ps1') -Version $Version
& $compiler "/DMyAppVersion=$Version" (Join-Path $repoRoot 'installer\Trag.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)." }
Write-Host "Installer: $(Join-Path $repoRoot "dist\Trag-$Version-win-x64-setup.exe")"
