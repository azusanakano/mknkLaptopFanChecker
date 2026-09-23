param(
    [string]$OutputDirectory = ".."
)

$ErrorActionPreference = "Stop"
$sourceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$output = [System.IO.Path]::GetFullPath((Join-Path $sourceRoot $OutputDirectory))
New-Item -ItemType Directory -Force -Path $output | Out-Null

$compiler = Get-Command cl.exe -ErrorAction SilentlyContinue
$resourceCompiler = Get-Command rc.exe -ErrorAction SilentlyContinue
if (-not $compiler -or -not $resourceCompiler) {
    throw "Visual Studio 2022 Build Tools の x64 Native Tools Command Prompt から実行してください。"
}

Push-Location $sourceRoot
try {
    & rc.exe /nologo /fo "$output\launcher.res" launcher.rc
    if ($LASTEXITCODE -ne 0) { throw "リソースのコンパイルに失敗しました。" }

    & cl.exe /nologo /std:c++20 /O2 /EHsc /MT /DUNICODE /D_UNICODE /utf-8 `
        launcher.cpp "$output\launcher.res" /Fe:"$output\mknkLaptopFanChecker.exe" `
        /link /SUBSYSTEM:WINDOWS /MACHINE:X64 shell32.lib user32.lib
    if ($LASTEXITCODE -ne 0) { throw "ネイティブランチャーのビルドに失敗しました。" }
} finally {
    Pop-Location
}

Remove-Item "$output\launcher.res" -ErrorAction SilentlyContinue
Write-Host "Built: $output\mknkLaptopFanChecker.exe"
