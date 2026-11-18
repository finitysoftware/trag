param(
    [string]$Project = 'src/Trag.Desktop',
    [string]$Output = 'artifacts/publish/win-x64',
    # A release stamps its version on Trag.exe; without one the build carries the version in Directory.Build.props.
    [string]$Version = ''
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
$originalPath = $env:PATH
$originalLib = $env:LIB
try {
    $locator = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (!(Test-Path -LiteralPath $locator)) { throw 'Visual Studio C++ Build Tools are required.' }
    $installation = & $locator -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (!$installation) { throw 'No C++ x64 toolchain was found.' }
    $vcVersion = (Get-Content -LiteralPath (Join-Path $installation 'VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt')).Trim()
    $vcRoot = Join-Path $installation "VC/Tools/MSVC/$vcVersion"
    $sdkRoot = (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots').KitsRoot10
    $sdkVersion = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Lib') -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'um/x64/kernel32.lib') } |
        Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty Name
    if (!$sdkVersion) { throw 'Windows SDK x64 libraries were not found.' }
    $env:PATH = "$vcRoot\bin\Hostx64\x64;$originalPath"
    $env:LIB = "$vcRoot\lib\x64;$sdkRoot\Lib\$sdkVersion\ucrt\x64;$sdkRoot\Lib\$sdkVersion\um\x64"
    $stamp = @(if ($Version) { "-p:Version=$Version" })
    dotnet publish $Project -c Release -r win-x64 -p:RestoreLockedMode=true -p:IlcUseEnvironmentalTools=true @stamp -o $Output
    if ($LASTEXITCODE -ne 0) { throw "NativeAOT publish failed ($LASTEXITCODE)." }
}
finally {
    $env:PATH = $originalPath
    $env:LIB = $originalLib
    Pop-Location
}
