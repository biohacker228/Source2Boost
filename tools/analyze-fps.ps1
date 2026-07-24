<#
  Разбор CSV от PresentMon: avg FPS, 1% low, 0.1% low, макс. фреймтайм (стуттер), стабильность.
  Использование:
    .\analyze-fps.ps1 before.csv                 # один замер
    .\analyze-fps.ps1 before.csv after.csv        # сравнение ДО/ПОСЛЕ
    .\analyze-fps.ps1                             # взять последние before_* и after_* из captures\
#>
param([string]$A, [string]$B)

function Get-Frametimes([string]$path) {
  if (-not (Test-Path $path)) { throw "Нет файла: $path" }
  $rows = Import-Csv $path
  # PresentMon v1_metrics: колонка msBetweenPresents (фреймтайм, мс)
  $col = ($rows | Get-Member -MemberType NoteProperty | Where-Object { $_.Name -match 'msBetweenPresents' } | Select-Object -First 1).Name
  if (-not $col) { $col = ($rows | Get-Member -MemberType NoteProperty | Where-Object { $_.Name -match 'FrameTime|MsBetweenDisplayChange' } | Select-Object -First 1).Name }
  if (-not $col) { throw "Не нашёл колонку фреймтайма в $path" }
  ,@($rows | ForEach-Object { [double]$_.$col } | Where-Object { $_ -gt 0 })
}

function Get-Stats($ft) {
  $sorted = $ft | Sort-Object
  $n = $sorted.Count
  if ($n -lt 10) { throw "Слишком мало кадров ($n)." }
  $sum = ($ft | Measure-Object -Sum).Sum
  $mean = $sum / $n
  # перцентили ФРЕЙМТАЙМА (worst) -> 1% low FPS = 1000 / p99 frametime
  function Pct($p) { $sorted[[Math]::Min($n-1, [int][Math]::Floor($p/100.0*$n))] }
  $p99  = Pct 99
  $p999 = Pct 99.9
  $sd = [Math]::Sqrt( ($ft | ForEach-Object { ($_-$mean)*($_-$mean) } | Measure-Object -Sum).Sum / $n )
  [pscustomobject]@{
    Frames      = $n
    AvgFps      = [Math]::Round(1000.0/$mean,1)
    Low1Fps     = [Math]::Round(1000.0/$p99,1)
    Low01Fps    = [Math]::Round(1000.0/$p999,1)
    MaxStutter  = [Math]::Round(($sorted[-1]),1)   # худший фреймтайм, мс
    FtStdDev    = [Math]::Round($sd,2)             # разброс фреймтайма (меньше = плавнее)
  }
}

# авто-подбор последних файлов
$capDir = Join-Path $PSScriptRoot 'captures'
if (-not $A -and (Test-Path $capDir)) {
  $A = (Get-ChildItem $capDir -Filter 'before_*.csv' | Sort-Object LastWriteTime | Select-Object -Last 1).FullName
  $B = (Get-ChildItem $capDir -Filter 'after_*.csv'  | Sort-Object LastWriteTime | Select-Object -Last 1).FullName
}
if (-not $A) { Write-Host "Укажи CSV: .\analyze-fps.ps1 <before.csv> [after.csv]"; return }

$sa = Get-Stats (Get-Frametimes $A)
Write-Host ""
Write-Host ("ДО   : avg {0}  | 1% low {1}  | 0.1% low {2}  | макс.стуттер {3} мс  | разброс {4}" -f `
  $sa.AvgFps,$sa.Low1Fps,$sa.Low01Fps,$sa.MaxStutter,$sa.FtStdDev) -ForegroundColor White

if ($B -and (Test-Path $B)) {
  $sb = Get-Stats (Get-Frametimes $B)
  Write-Host ("ПОСЛЕ: avg {0}  | 1% low {1}  | 0.1% low {2}  | макс.стуттер {3} мс  | разброс {4}" -f `
    $sb.AvgFps,$sb.Low1Fps,$sb.Low01Fps,$sb.MaxStutter,$sb.FtStdDev) -ForegroundColor Green
  function Delta($x,$y,[bool]$lowerBetter=$false){ $d = [Math]::Round($y-$x,1); $p = if($x){[Math]::Round(($y-$x)/$x*100,1)}else{0}; $good = if($lowerBetter){$d -lt 0}else{$d -gt 0}; $sign = if($d -ge 0){'+'}else{''}; return @("$sign$d ($sign$p%)",$good) }
  Write-Host ""
  Write-Host "ИЗМЕНЕНИЕ:" -ForegroundColor Cyan
  foreach ($m in @(
      @('avg FPS',$sa.AvgFps,$sb.AvgFps,$false),
      @('1% low',$sa.Low1Fps,$sb.Low1Fps,$false),
      @('0.1% low',$sa.Low01Fps,$sb.Low01Fps,$false),
      @('макс.стуттер',$sa.MaxStutter,$sb.MaxStutter,$true),
      @('разброс ФТ',$sa.FtStdDev,$sb.FtStdDev,$true))) {
    $r = Delta $m[1] $m[2] $m[3]
    $c = if($r[1]){'Green'}else{'Red'}
    Write-Host ("  {0,-14} {1}" -f $m[0], $r[0]) -ForegroundColor $c
  }
}
Write-Host ""
