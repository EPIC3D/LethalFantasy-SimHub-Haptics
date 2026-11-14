# Builds the plugin in Release and creates dist\LethalFantasy-SimHub-Haptics-<version>.zip
# Usage: .\scripts\package.ps1 [-LethalFantasyDir "D:\Games\LethalFantasy"]
param(
    [string]$LethalFantasyDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\SimHubTelemetry\SimHubTelemetry.csproj'

$buildArgs = @('build', $project, '-c', 'Release', '--nologo')
if ($LethalFantasyDir) { $buildArgs += "-p:LethalFantasyDir=$LethalFantasyDir" }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

[xml]$csproj = Get-Content $project
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$name = "LethalFantasy-SimHub-Haptics-$version"

$dist = Join-Path $root 'dist'
$stage = Join-Path $dist $name
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

Copy-Item (Join-Path $root 'src\SimHubTelemetry\bin\Release\netstandard2.1\SimHubTelemetry.dll') $stage
Copy-Item (Join-Path $root 'simhub\LethalFantasy.simdef') $stage
Copy-Item (Join-Path $root 'simhub\LethalFantasy-HF8.siprofile') $stage
Copy-Item (Join-Path $root 'config\com.epicopy.copilot.simhubtelemetry.cfg.example') $stage
Copy-Item (Join-Path $root 'docs\INSTALL.md') $stage
Copy-Item (Join-Path $root 'docs\CONFIGURATION.md') $stage
Copy-Item (Join-Path $root 'CHANGELOG.md') $stage
Copy-Item (Join-Path $root 'LICENSE') $stage

$zip = Join-Path $dist "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "Created $zip"
