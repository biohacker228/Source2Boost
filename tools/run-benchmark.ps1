<#
  АВТОНОМНЫЙ A/B-бенчмарк CS2 через детерминированный реплей демки.
  Идея: демка проигрывается ОДИНАКОВО каждый раз → честное сравнение до/после без ручной игры.

  Как получить демку один раз (в игре, консоль):
    record bench            // начать запись (в DM/на карте побегать ~1-2 мин)
    stop                    // остановить -> demo появится в ...\game\csgo\bench.dem

  Использование:
    .\run-benchmark.ps1 -Demo bench -Label before -Seconds 90
    # применить твики в приложении, при необходимости перезагрузиться, затем:
    .\run-benchmark.ps1 -Demo bench -Label after  -Seconds 90
    .\analyze-fps.ps1     # авто-сравнит последние before/after

  Замечание: некоторые агрессивные твики (Spectre off, bcdedit, timers) встают только ПОСЛЕ РЕБУТА,
  поэтому полностью автономный прогон «применил->померил» без перезагрузки для них неполон.
#>
param(
  [Parameter(Mandatory=$true)][string]$Demo,
  [ValidateSet('before','after')][string]$Label = 'before',
  [int]$Seconds = 90,
  [int]$LoadWait = 15   # сек на загрузку демки перед началом замера
)

$pm  = Join-Path $PSScriptRoot 'PresentMon-2.5.1-x64.exe'
$dir = Join-Path $PSScriptRoot 'captures'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$csv = Join-Path $dir ("{0}_{1}.csv" -f $Label, (Get-Date -Format 'yyyyMMdd_HHmmss'))

# 1) найти cs2.exe (Steam -> libraryfolders.vdf -> CS2)
function Find-Cs2 {
  try {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction Stop).SteamPath
  } catch { return $null }
  $steam = $steam -replace '/','\'
  $libs = @($steam)
  $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
  if (Test-Path $vdf) {
    Get-Content $vdf | Select-String '"path"\s+"(.+?)"' | ForEach-Object {
      $libs += ($_.Matches.Groups[1].Value -replace '\\\\','\')
    }
  }
  foreach ($l in $libs | Select-Object -Unique) {
    $p = Join-Path $l 'steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe'
    if (Test-Path $p) { return $p }
  }
  return $null
}

$cs2 = Find-Cs2
if (-not $cs2) { Write-Host "Не нашёл cs2.exe. Убедись, что CS2 установлен и Steam запускался." -ForegroundColor Red; return }
if (-not (Test-Path $pm)) { Write-Host "Нет PresentMon: $pm" -ForegroundColor Red; return }

# 2) закрыть старый cs2, запустить реплей демки
Get-Process cs2 -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Write-Host "Запуск CS2 с реплеем демки '$Demo'..." -ForegroundColor Cyan
Start-Process -FilePath $cs2 -ArgumentList "-novid","-console","-high","+playdemo","$Demo"

# 3) ждать загрузки, затем захват PresentMon
Write-Host "Жду загрузки $LoadWait сек..." -ForegroundColor DarkGray
Start-Sleep -Seconds $LoadWait
if (-not (Get-Process cs2 -ErrorAction SilentlyContinue)) { Write-Host "cs2.exe не запустился/закрылся. Проверь имя демки." -ForegroundColor Red; return }

Write-Host "Замер '$Label' на $Seconds сек..." -ForegroundColor Cyan
$pmArgs = @('--process_name','cs2.exe','--output_file',"`"$csv`"",'--timed',"$Seconds",
            '--terminate_after_timed','--stop_existing_session','--v1_metrics','--no_console_stats')
Start-Process -FilePath $pm -ArgumentList $pmArgs -Verb RunAs -Wait

# 4) закрыть игру
Get-Process cs2 -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if (Test-Path $csv) {
  Write-Host "Готово: $csv" -ForegroundColor Green
  Write-Host "Сравнение: .\analyze-fps.ps1"
} else {
  Write-Host "CSV не создан — PresentMon не увидел кадров cs2.exe." -ForegroundColor Red
}
