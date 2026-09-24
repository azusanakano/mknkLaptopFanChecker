param(
    [string]$Version = '1.3.1',
    [string]$OutputDirectory = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
$packageName = "NekoSystem-PCFanChecker-$Version-win64"
$stage = Join-Path $outputRoot $packageName
$archive = Join-Path $outputRoot ($packageName + '.zip')

if (-not (Test-Path (Join-Path $projectRoot 'mknkLaptopFanChecker.exe'))) {
    throw 'Native launcher is missing. Build it before packaging.'
}
if (-not (Test-Path (Join-Path $projectRoot 'runtime\mknkLaptopFanChecker.exe'))) {
    throw 'Managed application is missing. Run Build-Windows.ps1 before packaging.'
}

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $archive) { Remove-Item $archive -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$files = @(
    'mknkLaptopFanChecker.exe', 'app.ico', 'README.md', 'RELEASE_NOTES.md',
    'LICENSE.txt', 'NATIVE_COMPONENTS.md', 'SECURITY.md', 'THIRD_PARTY_NOTICES.md'
)
foreach ($file in $files) {
    Copy-Item (Join-Path $projectRoot $file) (Join-Path $stage $file) -Force
}
Copy-Item (Join-Path $projectRoot 'runtime') (Join-Path $stage 'runtime') -Recurse -Force
Copy-Item (Join-Path $projectRoot 'licenses') (Join-Path $stage 'licenses') -Recurse -Force
Copy-Item (Join-Path $projectRoot 'resources') (Join-Path $stage 'resources') -Recurse -Force

$checksumPath = Join-Path $stage 'CHECKSUMS.txt'
$stageLength = $stage.Length + 1
Get-ChildItem $stage -File -Recurse |
    Where-Object { $_.FullName -ne $checksumPath } |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($stageLength).Replace('\', '/')
        $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relative"
    } | Set-Content $checksumPath -Encoding ascii

Compress-Archive -Path $stage -DestinationPath $archive -CompressionLevel Optimal
Write-Host "Created: $archive"
