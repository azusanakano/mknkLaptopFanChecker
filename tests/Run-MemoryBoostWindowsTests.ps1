$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $PSScriptRoot 'bin\manual\MemoryBoostWindowsTests.exe'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null
& $compiler /nologo /target:exe /warnaserror+ /codepage:65001 "/out:$output" /reference:System.dll /reference:System.Core.dll (Join-Path $PSScriptRoot '..\src\MemoryBoost.cs') (Join-Path $PSScriptRoot 'MemoryBoostWindowsTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Windows memory test compilation failed' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Windows memory test failed' }
