param()

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$dist = Join-Path $root 'dist'
$publishDir = Join-Path $dist 'win-x64'
$zipPath = Join-Path $dist 'ExocadDailyExporter-win-x64.zip'

if (Test-Path $dist) {
    Remove-Item $dist -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir | Out-Null

Write-Host 'Restore packages...'
dotnet restore ..\ExocadDailyExporter.sln

Write-Host 'Publish self-contained package...'
dotnet publish ..\ExocadDailyExporter.sln -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $publishDir

Write-Host 'Copy configuration and README...'
Copy-Item (Join-Path $root 'appsettings.json') $publishDir -Force
Copy-Item (Join-Path $root 'README.md') $publishDir -Force

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Write-Host 'Create zip package...'
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath

$downloadLink = "file:///$($zipPath.Replace('\', '/'))"
Set-Content -Path (Join-Path $dist 'download-link.txt') -Value $downloadLink -Encoding UTF8

Write-Host "打包完成: $zipPath"
Write-Host "下载链接: $downloadLink"
