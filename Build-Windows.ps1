param(
    [switch]$Run,
    [switch]$Demo
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$runtimeDir = Join-Path $projectRoot 'runtime'
$outputPath = Join-Path $runtimeDir 'mknkLaptopFanChecker.exe'
$manifestPath = Join-Path $projectRoot 'app.manifest'
$iconPath = Join-Path $projectRoot 'app.ico'
$configPath = Join-Path $projectRoot 'App.config'
$libraryPath = Join-Path $runtimeDir 'LibreHardwareMonitorLib.dll'

$compilerCandidates = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$compiler = $compilerCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $compiler) {
    throw '.NET Framework C# compiler was not found. Enable .NET Framework 4.8 in Windows Features.'
}
if (-not (Test-Path $libraryPath)) {
    throw 'LibreHardwareMonitorLib.dll is missing from the runtime folder.'
}
if (-not (Test-Path $iconPath)) {
    throw 'app.ico is missing from the application folder.'
}

$sources = Get-ChildItem (Join-Path $projectRoot 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName }
$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/deterministic+',
    '/warn:4',
    '/warnaserror+',
    '/codepage:65001',
    "/out:$outputPath",
    "/win32manifest:$manifestPath",
    "/win32icon:$iconPath",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Management.dll',
    '/reference:System.Xml.dll',
    '/reference:System.Windows.Forms.dll',
    "/reference:$libraryPath"
) + $sources

Write-Host 'Building mknkLaptopFanChecker...'
& $compiler $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}
Copy-Item $configPath ($outputPath + '.config') -Force
Write-Host "Built: $outputPath"

if ($Run) {
    $runArguments = if ($Demo) { '--demo' } else { '' }
    Start-Process -FilePath $outputPath -ArgumentList $runArguments
}
