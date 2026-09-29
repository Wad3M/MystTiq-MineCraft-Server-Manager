$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\ASimpleMinecraftServer\ASimpleMinecraftServer.csproj'

Write-Host '=== MystTiq Minecraft Server Manager Release Validation ===' -ForegroundColor Cyan
Write-Host 'Cleaning...'
dotnet clean $project -c Release
Write-Host 'Restoring...'
dotnet restore $project
Write-Host 'Building with warnings visible...'
dotnet build $project -c Release --no-restore -warnaserror
Write-Host 'Publishing Windows x64...'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true --no-build

$publish = Join-Path $PSScriptRoot 'src\ASimpleMinecraftServer\bin\Release\net10.0-windows\win-x64\publish'
$exe = Join-Path $publish 'ASimpleMinecraftServer.exe'
if (-not (Test-Path $exe)) { throw "Published executable was not found: $exe" }

Write-Host ''
Write-Host 'Validation passed.' -ForegroundColor Green
Write-Host "Published executable: $exe"
