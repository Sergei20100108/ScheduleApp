using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScheduleApp.Data;
using ScheduleApp.Domain;
using ScheduleApp.Services.Data;
using ScheduleApp.Services.Dialogs;
using ScheduleApp.Services.Export;
using ScheduleApp.Services.Scheduling;
using ScheduleApp.Services.Settings;
using ScheduleApp.Services.Validation;
using ScheduleApp.ViewModels;
using ScheduleApp.ViewModels.ScheduleGrid;

namespace VmCheck
{
    internal sealed class FakeDialogService : IDialogService
    {
        public void Info(string message, string title = "Расписание") => Console.WriteLine("[INFO] " + message);
        public void Warn(string message, string title = "Предупреждение") => Console.WriteLine("[WARN] " + message);
        public void Error(string message, string title = "Ошибка") => Console.WriteLine("[ERROR] " + message);
        public bool Confirm(string message, string title = "Подтверждение") { Console.WriteLine("[CONFIRM] " + message); return true; }
        public string SaveFilePath(string filter, string defaultName) => null;
        public string OpenFilePath(string filter) => null;
    }

    internal static class Program
    {
        private static int Main() => MainAsync().GetAwaiter().GetResult();

        private static async Task<int> MainAsync()
        {
            try
            {
                var services = new ServiceCollection();
                services.AddDbContext<ScheduleDbContext>(o =>
                    o.UseSqlite(DbConnectionInfo.ConnectionString), ServiceLifetime.Transient);
                services.AddSingleton<ISettingsService, JsonSettingsService>();
                services.AddSingleton<IDialogService, FakeDialogService>();
                services.AddSingleton<SchedulingAlgorithmRegistry>();
                services.AddSingleton<IScheduleDataService, ScheduleDataService>();
                services.AddSingleton<IValidationService, ValidationService>();
                services.AddSingleton<IGenerationService, GenerationService>();
                services.AddSingleton<IExcelExportService, ExcelExportService>();
                services.AddSingleton<IPdfExportService, PdfExportService>();
                services.AddSingleton<MainViewModel>();

                using (var sp = services.BuildServiceProvider())
                {
                    using (var scope = sp.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                        DbInitializer.Initialize(db);
                        Console.WriteLine($"RAW DB: Teachers={db.Teachers.Count()} Rooms={db.Auditoriums.Count()} " +
                                          $"Groups={db.Groups.Count()} Periods={db.Periods.Count()} Lessons={db.Lessons.Count()}");
                        foreach (var pc in db.Lessons.GroupBy(l => l.PeriodId).Select(g => new { g.Key, N = g.Count() }).ToList())
                            Console.WriteLine($"  period {pc.Key}: {pc.N} lessons");
                        Console.WriteLine($"RAW DB path: {DbConnectionInfo.DatabasePath}");
                    }

                    var vm = sp.GetRequiredService<MainViewModel>();
                    var sw = Stopwatch.StartNew();
                    Console.WriteLine("Initializing MainViewModel…");
                    await vm.InitializeAsync();
                    sw.Stop();
                    Console.WriteLine($"InitializeAsync elapsed: {sw.Elapsed.TotalSeconds:F1}s");
                    Console.WriteLine($"Main.StatusText: {vm.StatusText}");

                    var s = vm.Schedule;
                    Console.WriteLine($"Schedule.StatusText: {s.StatusText}");
                    Console.WriteLine($"Schedule.Period: {(s.Period == null ? "(null)" : s.Period.Name)}");
                    Console.WriteLine($"Schedule.Periods: {s.Periods.Count}");
                    Console.WriteLine($"Grid.Sections: {s.Grid.Sections.Count}");
                    var tiles = s.Grid.Sections.Sum(sec => sec.Rows.Sum(r => r.Cells.Sum(c => c.Tiles.Count)));
                    Console.WriteLine($"Grid tiles: {tiles}");
                    Console.WriteLine($"Grid.Title: '{s.Grid.Title}'");

                    // Exercise each view mode to confirm grid rebuilds
                    foreach (var mode in new[] { GridViewMode.Week, GridViewMode.Month, GridViewMode.ByAuditorium, GridViewMode.ByTeacher, GridViewMode.Summary })
                    {
                        s.ViewMode = mode;
                        var t = s.Grid.Sections.Sum(sec => sec.Rows.Sum(r => r.Cells.Sum(c => c.Tiles.Count)));
                        Console.WriteLine($"  mode {mode}: sections={s.Grid.Sections.Count} tiles={t}");
                    }

                    Console.WriteLine($"Reports periods: {vm.Reports.Periods.Count}");
                    Console.WriteLine($"Dictionaries areas: {vm.Dictionaries.AreaNames.Count} ({string.Join(", ", vm.Dictionaries.AreaNames)})");
                    foreach (var area in vm.Dictionaries.AreaNames)
                    {
                        vm.Dictionaries.SelectedArea = area;
                        Console.WriteLine($"  dict '{area}': rows={vm.Dictionaries.CurrentArea.Rows.Count}");
                    }

                    // ByAuditorium: выбираем аудиторию, в которой реально есть занятия
                    int busiestRoomId = 0;
                    using (var scope = sp.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                        busiestRoomId = db.Lessons.Where(l => l.PeriodId == s.Period.Id)
                            .GroupBy(l => l.AuditoriumId).Select(g => new { g.Key, N = g.Count() })
                            .OrderByDescending(x => x.N).First().Key;
                    }
                    s.ViewMode = GridViewMode.ByAuditorium;
                    s.ContextRoom = s.Rooms.FirstOrDefault(r => r.Value != null && r.Value.Id == busiestRoomId);
                    var ta = s.Grid.Sections.Sum(sec => sec.Rows.Sum(r => r.Cells.Sum(c => c.Tiles.Count)));
                    Console.WriteLine($"  ByAuditorium (busiest room {busiestRoomId}): sections={s.Grid.Sections.Count} tiles={ta} title='{s.Grid.Title}'");

                    s.Transposed = true;
                    var tat = s.Grid.Sections.Sum(sec => sec.Rows.Sum(r => r.Cells.Sum(c => c.Tiles.Count)));
                    Console.WriteLine($"  ByAuditorium transposed: tiles={tat}");
                    s.Transposed = false;

                    // Переключение периода на "Текущая неделя"
                    var week = s.Periods.FirstOrDefault(p => p.Value != null && p.Value.Name.Contains("недел"));
                    if (week != null)
                    {
                        s.ViewMode = GridViewMode.Week;
                        s.Period = week.Value;
                        s.ReloadLessons();
                        var tw = s.Grid.Sections.Sum(sec => sec.Rows.Sum(r => r.Cells.Sum(c => c.Tiles.Count)));
                        Console.WriteLine($"  Week period '{week.Value.Name}': tiles={tw}");
                    }

                    Console.WriteLine("VM CHECK: OK");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("VM EXCEPTION: " + ex);
                return 1;
            }
        }
    }
}
