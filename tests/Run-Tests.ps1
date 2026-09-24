$ErrorActionPreference = 'Stop'
$testRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
[xml]$project = Get-Content -LiteralPath (Join-Path $testRoot 'EvaluatorTests.csproj') -Raw
$sources = @($project.Project.ItemGroup.Compile | ForEach-Object { Join-Path $testRoot $_.Include })
$output = Join-Path $testRoot 'bin\manual\EvaluatorTests.exe'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null
$arguments = @('/nologo', '/target:exe', '/warnaserror+', '/codepage:65001', "/out:$output",
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Xml.dll') + $sources
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
