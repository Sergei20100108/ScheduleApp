using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScheduleApp.Data;
using ScheduleApp.Domain;
using ScheduleApp.Domain.Entities;
using ScheduleApp.Services.Data;
using ScheduleApp.Services.Export;
using ScheduleApp.Services.Scheduling;
using ScheduleApp.Services.Settings;
using ScheduleApp.Services.Validation;
using ScheduleApp.ViewModels.ScheduleGrid;

namespace DbCheck
{
    internal static class Program
    {
        private static int _failures;

        private static int Main() => MainAsync().GetAwaiter().GetResult();

        private static async Task<int> MainAsync()
        {
            try
            {
                var services = new ServiceCollection();
                services.AddDbContext<ScheduleDbContext>(o =>
                    o.UseSqlite(DbConnectionInfo.ConnectionString), ServiceLifetime.Transient);
                services.AddSingleton<ISettingsService, JsonSettingsService>();
                services.AddSingleton<IValidationService, ValidationService>();
                services.AddSingleton<IScheduleDataService, ScheduleDataService>();
                services.AddSingleton<IExcelExportService, ExcelExportService>();
                services.AddSingleton<IPdfExportService, PdfExportService>();
                services.AddSingleton<SchedulingAlgorithmRegistry>();
                services.AddSingleton<IGenerationService, GenerationService>();

                using (var sp = services.BuildServiceProvider())
                {
                    using (var scope = sp.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                        DbInitializer.Initialize(db);
                        Console.WriteLine("== Seed counts ==");
                        Console.WriteLine("Teachers=" + db.Teachers.Count() + " Auditoriums=" + db.Auditoriums.Count() +
                            " Disciplines=" + db.Disciplines.Count() + " Groups=" + db.Groups.Count() +
                            " Slots=" + db.TimeSlots.Count() + " Periods=" + db.Periods.Count() +
                            " PlanItems=" + db.StudyPlanItems.Count() + " Prefs=" + db.TeacherPreferences.Count() +
                            " HardPrefs=" + db.TeacherPreferences.Count(p => p.IsHard));
                    }

                    var gen = sp.GetRequiredService<IGenerationService>();
                    var validation = sp.GetRequiredService<IValidationService>();
                    var data = sp.GetRequiredService<IScheduleDataService>();

                    var periods = data.Load(0, includeLessons: false).Periods;
                    var semester = periods.First(p => p.Type == PeriodType.Semester);
                    var month = periods.First(p => p.Type == PeriodType.Month);
                    var week = periods.First(p => p.Type == PeriodType.Week);

                    // ---- 1. Algorithms: каждая стратегия на недельном периоде ----
                    Console.WriteLine();
                    Console.WriteLine("== Algorithms (week period) ==");
                    var registry = sp.GetRequiredService<SchedulingAlgorithmRegistry>();
                    foreach (var algo in registry.All)
                    {
                        var report = await GenerateRaw(gen, week.Id, GenerationMode.Full, ComplexityLevel.Fast, algo.Id);
                        var v = validation.ValidatePeriod(week.Id);
                        Console.WriteLine($"{algo.Id,-13} placed={report.Placed,4} unplaced={report.Unplaced,3} " +
                                          $"hard={v.HardViolations.Count,3} quality={v.QualityPercent,5:F1}% time={report.Elapsed.TotalSeconds,5:F1}s success={report.Success}");
                        Check(report.Success, $"algorithm '{algo.Id}' failed: {report.Message}");
                        Check(v.HardViolations.Count == 0, $"hard violations in algorithm '{algo.Id}'");
                    }

                    // ---- 2. Полная генерация семестра + месяц + неделя ----
                    Console.WriteLine();
                    Console.WriteLine("== Full generation ==");
                    foreach (var p in new[] { semester, month, week })
                    {
                        var report = await GenerateRaw(gen, p.Id, GenerationMode.Full, ComplexityLevel.Balanced);
                        var v = validation.ValidatePeriod(p.Id);
                        Console.WriteLine($"{p.Type,-9} '{p.Name}' placed={report.Placed} unplaced={report.Unplaced} " +
                                          $"hard={v.HardViolations.Count} warnings={v.Warnings.Count} quality={v.QualityPercent:F1}% " +
                                          $"lockedKept={report.LockedKept}");
                        Check(report.Success, $"generation failed for {p.Name}: {report.Message}");
                        Check(v.HardViolations.Count == 0, $"hard violations for {p.Name}");
                    }

                    // ---- 3. Заблокированные занятия не должны перемещаться при полной перегенерации ----
                    Console.WriteLine();
                    Console.WriteLine("== Locked lessons protection (full regeneration) ==");
                    var lockedIds = data.Load(week.Id).Lessons.OrderBy(l => l.Id).Take(15).Select(l => l.Id).ToList();
                    var before = new Dictionary<int, (WeekDay Day, int Slot, int Room)>();
                    foreach (var id in lockedIds)
                    {
                        var l = data.GetLesson(id);
                        l.IsLocked = true;
                        data.SaveLesson(l);
                        before[id] = (l.Day, l.TimeSlotId, l.AuditoriumId);
                    }

                    var reportLocked = await GenerateRaw(gen, week.Id, GenerationMode.Full, ComplexityLevel.Balanced);
                    var moved = 0;
                    foreach (var id in lockedIds)
                    {
                        var l = data.GetLesson(id);
                        if (l == null) { moved++; continue; }
                        if (l.Day != before[id].Day || l.TimeSlotId != before[id].Slot || l.AuditoriumId != before[id].Room) moved++;
                    }
                    Console.WriteLine($"locked={lockedIds.Count} moved={moved} lockedKept={reportLocked.LockedKept}");
                    Check(moved == 0, "locked lessons moved during full regeneration");
                    var vLocked = validation.ValidatePeriod(week.Id);
                    Check(vLocked.HardViolations.Count == 0, "hard violations after locked regeneration");

                    // ---- 4. Инкрементальный режим ----
                    Console.WriteLine();
                    Console.WriteLine("== Incremental mode ==");
                    var incBefore = data.Load(week.Id).Lessons.Count;
                    var inc = await GenerateRaw(gen, week.Id, GenerationMode.Incremental, ComplexityLevel.Balanced);
                    var vInc = validation.ValidatePeriod(week.Id);
                    var incAfter = data.Load(week.Id).Lessons.Count;
                    Console.WriteLine($"lessons before={incBefore} after={incAfter} placed={inc.Placed} hard={vInc.HardViolations.Count}");
                    Check(inc.Success, "incremental mode failed: " + inc.Message);
                    Check(vInc.HardViolations.Count == 0, "hard violations in incremental mode");
                    Check(incAfter >= incBefore, "incremental mode removed lessons");

                    // ---- 5. Режим добивки пустых слотов ----
                    Console.WriteLine();
                    Console.WriteLine("== FillEmpty mode ==");
                    var fillBefore = data.Load(week.Id).Lessons.Count;
                    var fill = await GenerateRaw(gen, week.Id, GenerationMode.FillEmpty, ComplexityLevel.Balanced);
                    var vFill = validation.ValidatePeriod(week.Id);
                    var fillAfter = data.Load(week.Id).Lessons.Count;
                    Console.WriteLine($"lessons before={fillBefore} after={fillAfter} placed={fill.Placed} hard={vFill.HardViolations.Count}");
                    Check(fill.Success, "fill-empty mode failed: " + fill.Message);
                    Check(vFill.HardViolations.Count == 0, "hard violations in fill-empty mode");

                    // ---- 6. ValidateOnly ----
                    Console.WriteLine();
                    Console.WriteLine("== ValidateOnly mode ==");
                    var vo = await GenerateRaw(gen, week.Id, GenerationMode.ValidateOnly, ComplexityLevel.Balanced);
                    Console.WriteLine($"success={vo.Success} hard={vo.HardViolations} message={vo.Message}");
                    Check(vo.Success, "validate-only mode failed");

                    // ---- 7. Шахматка: все режимы отображения ----
                    Console.WriteLine();
                    Console.WriteLine("== Grid building (all modes) ==");
                    foreach (var p in new[] { semester, month, week })
                    {
                        var snap = data.Load(p.Id);
                        var lessonIds = new HashSet<int>();
                        var modes = new[]
                        {
                            GridViewMode.Week, GridViewMode.Month, GridViewMode.Summary,
                            GridViewMode.ByAuditorium, GridViewMode.ByTeacher
                        };
                        foreach (var mode in modes)
                        {
                            foreach (var transposed in new[] { false, true })
                            {
                                var req = BuildRequest(snap, p, mode, transposed, lessonIds);
                                var grid = ScheduleGridBuilder.Build(req);
                                var tileCount = grid.Sections.Sum(s => s.Rows.Sum(r => r.Cells.Sum(c => c.Tiles.Count)));
                                Console.WriteLine($"{p.Type,-9} {mode,-13} transposed={transposed,-5} sections={grid.Sections.Count,3} tiles={tileCount,4} title='{grid.Title}'");
                                Check(grid.Sections.Count > 0, $"no sections for {mode} (transposed={transposed})");
                            }
                        }
                    }

                    // ---- 8. Экспорт Excel и PDF ----
                    Console.WriteLine();
                    Console.WriteLine("== Export ==");
                    var weekSnap = data.Load(week.Id);
                    var gridWeek = ScheduleGridBuilder.Build(BuildRequest(weekSnap, week, GridViewMode.Week, false, new HashSet<int>()));
                    var exportModel = BuildExport(gridWeek);

                    var outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "exports");
                    Directory.CreateDirectory(outDir);
                    var xlsx = Path.Combine(outDir, "schedule.xlsx");
                    var pdf = Path.Combine(outDir, "schedule.pdf");

                    sp.GetRequiredService<IExcelExportService>().Export(exportModel, xlsx);
                    Console.WriteLine($"Excel: {new FileInfo(xlsx).Length} bytes -> {xlsx}");
                    Check(new FileInfo(xlsx).Length > 4000, "excel file too small");

                    sp.GetRequiredService<IPdfExportService>().Export(exportModel, pdf);
                    Console.WriteLine($"PDF:   {new FileInfo(pdf).Length} bytes -> {pdf}");
                    Check(new FileInfo(pdf).Length > 2000, "pdf file too small");

                    Console.WriteLine();
                    if (_failures == 0) Console.WriteLine("RESULT: OK");
                    else Console.WriteLine($"RESULT: FAIL ({_failures} checks failed)");
                    return _failures == 0 ? 0 : 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION: " + ex);
                return 2;
            }
        }

        private static async Task<GenerationReport> GenerateRaw(IGenerationService gen, int periodId,
            GenerationMode mode, ComplexityLevel complexity, string algorithmId = null)
        {
            return await gen.GenerateAsync(new GenerationRequest
            {
                PeriodId = periodId,
                Mode = mode,
                Complexity = complexity,
                AlgorithmId = algorithmId
            }, null, CancellationToken.None);
        }

        private static GridRequest BuildRequest(ScheduleSnapshot snap, SchedulePeriod period, GridViewMode mode,
            bool transposed, HashSet<int> conflictIds)
        {
            var req = new GridRequest
            {
                Mode = mode,
                Transposed = transposed,
                Period = period,
                Slots = snap.Slots,
                Lessons = snap.Lessons,
                AllowedDays = new List<WeekDay>
                {
                    WeekDay.Monday, WeekDay.Tuesday, WeekDay.Wednesday,
                    WeekDay.Thursday, WeekDay.Friday, WeekDay.Saturday
                },
                ConflictLessonIds = conflictIds
            };
            foreach (var r in snap.Rooms) req.RoomNames[r.Id] = r.Number;
            foreach (var t in snap.Teachers) req.TeacherNames[t.Id] = t.FullName;
            foreach (var g in snap.Groups) req.GroupNames[g.Id] = g.Name;
            foreach (var d in snap.Disciplines) req.DisciplineNames[d.Id] = d.Name;

            if (mode == GridViewMode.ByAuditorium)
                req.AuditoriumId = snap.Lessons.Select(l => l.AuditoriumId).FirstOrDefault();
            if (mode == GridViewMode.ByTeacher)
                req.TeacherId = snap.Lessons.Select(l => l.TeacherId).FirstOrDefault();
            if (mode == GridViewMode.Summary)
                req.SummaryDimension = SummaryDimension.Groups;
            if (mode == GridViewMode.Week && !transposed)
                req.Grouping = GroupingMode.None;
            return req;
        }

        private static ExportGridModel BuildExport(ScheduleGridViewModel grid)
        {
            var model = new ExportGridModel
            {
                Title = grid.Title,
                Subtitle = grid.Subtitle,
                GeneratedAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm")
            };
            foreach (var section in grid.Sections)
            {
                model.ColumnHeaders.Clear();
                foreach (var c in section.Columns)
                    model.ColumnHeaders.Add(string.IsNullOrEmpty(c.SubHeader) ? (c.Header ?? "") : c.Header + " " + c.SubHeader);
                foreach (var row in section.Rows)
                {
                    var er = new ExportRow { Header = row.Header ?? "" };
                    foreach (var cell in row.Cells)
                    {
                        var ec = new ExportCell();
                        foreach (var t in cell.Tiles)
                        {
                            ec.Tiles.Add(new ExportTile
                            {
                                ColorHex = t.ColorHex,
                                Text = string.Join("\n", new[] { t.Line1, t.Line2, t.Line3 }
                                    .Where(x => !string.IsNullOrWhiteSpace(x)))
                            });
                        }
                        er.Cells.Add(ec);
                    }
                    model.Rows.Add(er);
                }
            }
            return model;
        }

        private static void Check(bool condition, string message)
        {
            if (condition) return;
            _failures++;
            Console.WriteLine("   CHECK FAILED: " + message);
        }
    }
}
