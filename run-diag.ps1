$dir = "C:\Users\AlexAD\Documents\Default Project\ScheduleApp\src\ScheduleApp\bin\Debug\net472"
$exe = Join-Path $dir "ScheduleApp.exe"
$log = Join-Path $dir "scheduleapp.log"
$db  = Join-Path $dir "schedule.db"

if (Test-Path $log)  { Remove-Item $log -Force }
if (Test-Path $db)   { Remove-Item $db -Force }
Get-Process ScheduleApp -ErrorAction SilentlyContinue | Stop-Process -Force

$p = Start-Process -FilePath $exe -WorkingDirectory $dir -PassThru
Start-Sleep -Seconds 20

Write-Output "=== PROCESS ==="
Get-Process ScheduleApp -ErrorAction SilentlyContinue | Select-Object Id, MainWindowTitle

Write-Output "=== DB EXISTS: $(Test-Path $db) ==="
if (Test-Path $db) { Get-Item $db | Select-Object Length, LastWriteTime }

Write-Output "=== LOG ==="
if (Test-Path $log) { Get-Content $log } else { Write-Output "(no log)" }

Get-Process ScheduleApp -ErrorAction SilentlyContinue | Stop-Process -Force
