# build-release.ps1 — детерминированная локальная часть релиза Source2Boost.
# Убивает запущенное приложение, собирает оба конфига (sanity), делает self-contained publish
# и установщик Inno Setup, печатает путь/размер/SHA256. Git и gh — вручную по SKILL.md.
$ErrorActionPreference = 'Stop'

# Корень репозитория = два уровня вверх от .claude\skills\release\scripts
$root    = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$dotnet  = 'C:\Program Files\dotnet\dotnet.exe'
$iscc    = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
$appProj = Join-Path $root 'src\Source2Boost.App\Source2Boost.App.csproj'
$iss     = Join-Path $root 'installer\Source2Boost.iss'
$publish = Join-Path $root 'publish'
$setup   = Join-Path $root 'dist\Source2Boost-Setup.exe'

foreach ($p in @($dotnet, $iscc, $appProj, $iss)) {
    if (-not (Test-Path $p)) { throw "Не найдено: $p" }
}

Write-Host '== [1/5] Закрываю запущенное приложение (лочит Core.dll) ==' -ForegroundColor Cyan
try { Start-Process taskkill.exe -ArgumentList '/F','/IM','Source2Boost.exe' -Verb RunAs -Wait -ErrorAction Stop } catch {}
Start-Sleep -Milliseconds 500

Write-Host '== [2/5] Сборка Debug + Release (sanity-check) ==' -ForegroundColor Cyan
& $dotnet build $appProj -c Debug   --nologo -v q; if ($LASTEXITCODE) { throw 'Debug build упал' }
& $dotnet build $appProj -c Release --nologo -v q; if ($LASTEXITCODE) { throw 'Release build упал' }

Write-Host '== [3/5] Чистка publish/ и self-contained publish (win-x64) ==' -ForegroundColor Cyan
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
& $dotnet publish $appProj -c Release -r win-x64 --self-contained true -o $publish --nologo -v m
if ($LASTEXITCODE) { throw 'publish упал' }
if (-not (Test-Path (Join-Path $publish 'Source2Boost.exe'))) { throw 'publish без Source2Boost.exe' }

Write-Host '== [4/5] Сборка установщика (ISCC) ==' -ForegroundColor Cyan
& $iscc $iss | Select-Object -Last 3
if ($LASTEXITCODE) { throw 'ISCC упал' }
if (-not (Test-Path $setup)) { throw "Установщик не создан: $setup" }

Write-Host '== [5/5] Хеш и размер ==' -ForegroundColor Cyan
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
$size = [math]::Round((Get-Item $setup).Length / 1MB, 1)
$ver  = (Select-String -Path $appProj -Pattern '<Version>(.+?)</Version>').Matches.Groups[1].Value

Write-Host ''
Write-Host '===== RELEASE ARTIFACT =====' -ForegroundColor Green
Write-Host "VERSION : $ver"
Write-Host "PATH    : $setup"
Write-Host "SIZE    : $size MB"
Write-Host "SHA256  : $hash"
Write-Host '============================' -ForegroundColor Green
