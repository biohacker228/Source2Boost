<#
  Замер фреймтайма CS2 через PresentMon (для сравнения ДО/ПОСЛЕ оптимизации).
  Использование:
    .\capture-fps.ps1 before      # замер до применения твиков
    .\capture-fps.ps1 after       # замер после
    .\capture-fps.ps1 after 120   # своя длительность (сек)
#>
param(
  [ValidateSet('before','after')][string]$Label = 'after',
  [int]$Seconds = 90
)

$pm  = Join-Path $PSScriptRoot 'PresentMon-2.5.1-x64.exe'
$dir = Join-Path $PSScriptRoot 'captures'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$csv = Join-Path $dir ("{0}_{1}.csv" -f $Label, (Get-Date -Format 'yyyyMMdd_HHmmss'))

if (-not (Get-Process cs2 -ErrorAction SilentlyContinue)) {
  Write-Host "CS2 (cs2.exe) не запущен. Запусти игру и зайди в матч/карту, потом повтори." -ForegroundColor Yellow
  return
}

Write-Host ""
Write-Host ">>> Замер '$Label' на $Seconds сек. НАЧИНАЙ ИГРАТЬ активно:" -ForegroundColor Cyan
Write-Host "    двигайся, стреляй, крути мышью в оживлённом месте (одно и то же для before/after!)." -ForegroundColor Cyan
Write-Host ""

$pmArgs = @(
  '--process_name','cs2.exe',
  '--output_file', "`"$csv`"",
  '--timed', "$Seconds",
  '--terminate_after_timed',
  '--stop_existing_session',
  '--v1_metrics',
  '--no_console_stats'
)
Start-Process -FilePath $pm -ArgumentList $pmArgs -Verb RunAs -Wait

if (Test-Path $csv) {
  Write-Host "Готово. CSV: $csv" -ForegroundColor Green
  Write-Host "Разбор: .\analyze-fps.ps1 `"$csv`""
} else {
  Write-Host "CSV не создан — PresentMon не увидел кадров cs2.exe (проверь, что игра в фокусе и рендерит)." -ForegroundColor Red
}
