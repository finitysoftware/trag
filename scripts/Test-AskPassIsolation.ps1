param([string]$Executable = 'artifacts/publish/win-x64/Trag.exe')
$ErrorActionPreference = 'Stop'
$start = [System.Diagnostics.ProcessStartInfo]::new((Resolve-Path -LiteralPath $Executable).Path)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
# A plain argument string, so the check runs under Windows PowerShell 5.1 as well as PowerShell 7.
$start.Arguments = "`"Username for 'https://example.invalid': `""
$start.Environment['TRAG_ASKPASS'] = '1'
$start.Environment['TRAG_ASKPASS_PIPE'] = 'trag-' + [guid]::NewGuid().ToString('N')
$start.Environment['TRAG_ASKPASS_OPERATION'] = [guid]::NewGuid().ToString('N')
$process = [System.Diagnostics.Process]::Start($start)
try {
    Start-Sleep -Milliseconds 250
    $process.Refresh()
    if ($process.HasExited) { throw 'Askpass exited before its module inspection.' }
    if ($process.MainWindowHandle -ne 0) { throw 'Askpass created a window.' }
    $modules = @($process.Modules | ForEach-Object ModuleName)
    if ($modules -match 'Skia|HarfBuzz|av_libgles|coreclr|hostfxr|hostpolicy') {
        throw 'Askpass loaded a UI library or a managed runtime.'
    }
    if (!$process.WaitForExit(10000)) { throw 'Askpass did not respect its IPC timeout.' }
    if ($process.ExitCode -ne 2 -or $process.StandardOutput.ReadToEnd().Length -ne 0) {
        throw 'Unavailable IPC context did not fail closed.'
    }
    [pscustomobject]@{ Result = 'Passed'; MainWindow = $false; Modules = $modules; ExitCode = $process.ExitCode } | ConvertTo-Json
}
finally {
    if (!$process.HasExited) { $process.Kill($true) }
    $process.Dispose()
}
