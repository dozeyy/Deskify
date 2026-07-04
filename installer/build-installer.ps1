# Builds Deskify-Setup.exe: publishes Deskify.App self-contained, zips it as the
# installer's embedded payload, then publishes Deskify.Setup as a single exe.
# Run from anywhere: powershell -File installer\build-installer.ps1
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
if (-not $root) { $root = (Get-Item "$PSScriptRoot\..").FullName }
$appProj = Join-Path $root "src\Deskify.App\Deskify.App.csproj"
$setupProj = Join-Path $PSScriptRoot "Deskify.Setup\Deskify.Setup.csproj"
$payloadDir = Join-Path $PSScriptRoot "Deskify.Setup\payload"
$publishDir = Join-Path $payloadDir "_publish"
$zipPath = Join-Path $payloadDir "payload.zip"
$outDir = Join-Path $PSScriptRoot "out"

Write-Host "==> Publishing Deskify.App (self-contained, win-x64)" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $appProj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "Publishing Deskify.App failed." }

Write-Host "==> Zipping payload" -ForegroundColor Cyan
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "==> Publishing Deskify-Setup.exe" -ForegroundColor Cyan
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
dotnet publish $setupProj -c Release -r win-x64 --self-contained true -o $outDir
if ($LASTEXITCODE -ne 0) { throw "Publishing Deskify-Setup failed." }

Write-Host ""
Write-Host "Done: $outDir\Deskify-Setup.exe" -ForegroundColor Green
Get-Item (Join-Path $outDir "Deskify-Setup.exe") | Select-Object Name, @{N="SizeMB";E={[math]::Round($_.Length/1MB,1)}}
