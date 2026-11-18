param(
    [string]$OutputDirectory = "dist"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$candidates = @(
    (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
    (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)
$csc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) {
    throw ".NET Framework C# compiler (csc.exe) was not found."
}

$output = Join-Path $root $OutputDirectory
New-Item -ItemType Directory -Path $output -Force | Out-Null

& $csc /nologo /target:winexe /optimize+ /codepage:65001 `
    "/win32manifest:$root\app.manifest" `
    "/win32icon:$root\assets\app.ico" `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    "/out:$output\AutoClicker.exe" `
    "$root\AutoClicker.cs"

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

Write-Host "Built $output\AutoClicker.exe"
