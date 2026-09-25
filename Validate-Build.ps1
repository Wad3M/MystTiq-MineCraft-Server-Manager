$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\ASimpleMinecraftServer\ASimpleMinecraftServer.csproj'
Write-Host 'Cleaning project...' -ForegroundColor Cyan
dotnet clean $project
Write-Host 'Building project...' -ForegroundColor Cyan
dotnet build $project
Write-Host 'Build validation completed successfully.' -ForegroundColor Green
