$dir = "C:\Users\AlexAD\Documents\Default Project\ScheduleApp\src\ScheduleApp\bin\Debug\net472"
[System.IO.Directory]::SetCurrentDirectory($dir)
$env:PATH = "$dir;$env:PATH"

foreach ($dll in @("SQLitePCLRaw.core.dll","SQLitePCLRaw.provider.dynamic_cdecl.dll","SQLitePCLRaw.batteries_v2.dll","Microsoft.Data.Sqlite.dll")) {
    $p = Join-Path $dir $dll
    if (Test-Path $p) { [System.Reflection.Assembly]::LoadFrom($p) | Out-Null }
}

$cs = "Data Source=$dir\schedule.db"
$c = New-Object Microsoft.Data.Sqlite.SqliteConnection $cs
$c.Open()
$tables = @("Teachers","Auditoriums","Disciplines","Groups","TimeSlots","Periods","StudyPlanItems","TeacherPreferences","Lessons","LessonGroups")
foreach ($t in $tables) {
    $cmd = $c.CreateCommand()
    $cmd.CommandText = "SELECT COUNT(*) FROM $t"
    $n = $cmd.ExecuteScalar()
    Write-Output "$t = $n"
}
$cmd = $c.CreateCommand()
$cmd.CommandText = "SELECT COUNT(*) FROM TeacherPreferences WHERE IsHard = 1"
Write-Output ("HardPrefs = " + $cmd.ExecuteScalar())
$c.Close()
