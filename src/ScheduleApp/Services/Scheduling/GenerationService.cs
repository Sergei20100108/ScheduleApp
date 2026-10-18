using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScheduleApp.Data;
using ScheduleApp.Domain;
using ScheduleApp.Domain.Entities;
using ScheduleApp.Services.Settings;
using ScheduleApp.Services.Validation;

namespace ScheduleApp.Services.Scheduling
{
    public sealed class GenerationRequest
    {
        public int PeriodId;
        public GenerationMode Mode;
        public ComplexityLevel Complexity;

        /// <summary>Необязательный явный идентификатор стратегии (иначе выбирается по режиму сложности).</summary>
        public string AlgorithmId;
    }

    public sealed class GenerationProgress
    {
        public int Percent;
        public string Message;
    }

    public sealed class GenerationReport
    {
        public bool Success;
        public string AlgorithmName = "";
        public int TotalDemands;
        public int Placed;
        public int Unplaced;
        public int LockedKept;
        public double QualityPercent;
        public int HardViolations;
        public TimeSpan Elapsed;
        public string Message = "";
        public List<string> UnplacedLabels = new List<string>();
    }

    public interface IGenerationService
    {
        Task<GenerationReport> GenerateAsync(GenerationRequest request,
            IProgress<GenerationProgress> progress, CancellationToken ct);
    }

    public sealed class GenerationService : IGenerationService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly SchedulingAlgorithmRegistry _registry;
        private readonly ISettingsService _settings;
        private readonly IValidationService _validation;

        public GenerationService(IServiceScopeFactory scopeFactory, SchedulingAlgorithmRegistry registry,
            ISettingsService settings, IValidationService validation)
        {
            _scopeFactory = scopeFactory;
            _registry = registry;
            _settings = settings;
            _validation = validation;
        }

        public async Task<GenerationReport> GenerateAsync(GenerationRequest request,
            IProgress<GenerationProgress> progress, CancellationToken ct)
        {
            return await Task.Run(() => Execute(request, progress, ct), ct).ConfigureAwait(true);
        }

        private GenerationReport Execute(GenerationRequest request, IProgress<GenerationProgress> progress, CancellationToken ct)
        {
            var report = new GenerationReport();
            var sw = Stopwatch.StartNew();

            void Step(int pct, string msg)
            {
                progress?.Report(new GenerationProgress { Percent = pct, Message = msg });
                ct.ThrowIfCancellationRequested();
            }

            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                    var appSettings = _settings.Current;

                    Step(3, "Загрузка данных…");
                    var period = db.Periods.First(p => p.Id == request.PeriodId);
                    var slots = db.TimeSlots.AsNoTracking().OrderBy(s => s.Order).ToList();
                    var rooms = db.Auditoriums.AsNoTracking().ToList();
                    var teachers = db.Teachers.AsNoTracking().ToList();
                    var groups = db.Groups.AsNoTracking().ToList();
                    var disciplines = db.Disciplines.AsNoTracking().ToDictionary(d => d.Id);
                    var plan = db.StudyPlanItems.AsNoTracking().ToList();
                    var prefs = db.TeacherPreferences.AsNoTracking().ToList();
                    var existing = db.Lessons.AsNoTracking()
                        .Include(l => l.LessonGroups)
                        .Where(l => l.PeriodId == period.Id)
                        .ToList();

                    if (request.Mode == GenerationMode.ValidateOnly)
                    {
                        var v = _validation.ValidatePeriod(request.PeriodId);
                        Step(100, "Проверка завершена");
                        report.Success = true;
                        report.HardViolations = v.HardViolations.Count;
                        report.QualityPercent = v.QualityPercent;
                        report.TotalDemands = existing.Count;
                        report.Placed = existing.Count;
                        report.Message = v.HardViolations.Count == 0
                            ? "Жёсткие ограничения не нарушаются"
                            : $"Обнаружено нарушений: {v.HardViolations.Count}";
                        report.Elapsed = sw.Elapsed;
                        return report;
                    }

                    Step(8, "Построение модели задачи…");
                    var keptLessons = request.Mode == GenerationMode.Full
                        ? existing.Where(l => l.IsLocked).ToList()
                        : existing; // инкрементально и добивка сохраняют всё, что уже есть

                    var allowedDays = period.Type == PeriodType.Week
                        ? new[] { WeekDay.Monday, WeekDay.Tuesday, WeekDay.Wednesday, WeekDay.Thursday, WeekDay.Friday, WeekDay.Saturday }
                        : new[] { WeekDay.Monday, WeekDay.Tuesday, WeekDay.Wednesday, WeekDay.Thursday, WeekDay.Friday, WeekDay.Saturday };

                    var problem = BuildProblem(slots, rooms, teachers, groups, disciplines, plan, prefs, keptLessons, allowedDays, appSettings);
                    report.TotalDemands = problem.Demands.Length;
                    report.LockedKept = existing.Count(l => l.IsLocked);

                    // Удаляем незакреплённые занятия режима полной перегенерации
                    if (request.Mode == GenerationMode.Full)
                    {
                        var removable = existing.Where(l => !l.IsLocked).ToList();
                        db.Lessons.RemoveRange(removable);
                        db.SaveChanges();
                    }

                    var algorithm = request.Mode == GenerationMode.FillEmpty
                        ? _registry.ById("greedy")
                        : !string.IsNullOrEmpty(request.AlgorithmId)
                            ? _registry.ById(request.AlgorithmId)
                            : _registry.Resolve(request.Complexity, problem.Demands.Length);

                    Step(12, $"Расчёт ({algorithm.DisplayName})…");
                    var innerProgress = new Progress<SolveProgress>(sp =>
                        progress?.Report(new GenerationProgress
                        {
                            Percent = 12 + (int)(sp.Percent * 0.78),
                            Message = sp.Message
                        }));

                    var result = algorithm.Solve(problem, new SchedulingParams
                    {
                        TimeBudgetMs = TimeBudget(request.Complexity, problem.Demands.Length)
                    }, innerProgress, ct);

                    Step(92, "Сохранение результата…");
                    var existingByPlan = keptLessons
                        .Where(l => l.StudyPlanItemId.HasValue)
                        .GroupBy(l => l.StudyPlanItemId.Value)
                        .ToDictionary(g => g.Key, g => g.ToList());

                    var placedAssignments = result.Assignments
                        .Where(a => !keptLessons.Any(l => l.StudyPlanItemId.HasValue && a.Demand.PlanItemId == l.StudyPlanItemId.Value))
                        .ToList();

                    foreach (var a in result.Assignments)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (a.Demand.PlanItemId.HasValue && existingByPlan.ContainsKey(a.Demand.PlanItemId.Value))
                            continue; // уже покрыто существующим занятием
                        db.Lessons.Add(new Lesson
                        {
                            PeriodId = period.Id,
                            Day = (WeekDay)a.Day,
                            TimeSlotId = a.SlotId,
                            AuditoriumId = a.RoomId,
                            TeacherId = a.Demand.TeacherId,
                            DisciplineId = a.Demand.DisciplineId,
                            StudyPlanItemId = a.Demand.PlanItemId,
                            IsLocked = false,
                            LessonGroups = a.Demand.GroupIds.Select(g => new LessonGroup { GroupId = g }).ToList()
                        });
                    }
                    db.SaveChanges();

                    Step(96, "Проверка ограничений…");
                    var validation = _validation.ValidatePeriod(period.Id);

                    report.Success = true;
                    report.AlgorithmName = result.AlgorithmName;
                    report.Placed = problem.Demands.Length - result.Unplaced.Count;
                    report.Unplaced = result.Unplaced.Count;
                    report.UnplacedLabels = result.Unplaced.Select(d => d.Label).Take(40).ToList();
                    report.QualityPercent = validation.QualityPercent;
                    report.HardViolations = validation.HardViolations.Count;
                    report.Elapsed = sw.Elapsed;
                    report.Message = validation.HardViolations.Count == 0
                        ? "Готово: жёсткие ограничения не нарушаются"
                        : $"Внимание: обнаружено нарушений {validation.HardViolations.Count}";

                    Step(100, "Готово");
                }
            }
            catch (OperationCanceledException)
            {
                report.Success = false;
                report.Message = "Генерация отменена пользователем";
                progress?.Report(new GenerationProgress { Percent = 0, Message = report.Message });
            }
            catch (Exception ex)
            {
                report.Success = false;
                report.Message = $"Ошибка генерации: {ex.Message}";
            }
            report.Elapsed = sw.Elapsed;
            return report;
        }

        private static int TimeBudget(ComplexityLevel level, int demandCount)
        {
            var scale = Math.Max(4, demandCount / 120);
            switch (level)
            {
                case ComplexityLevel.Fast: return 5000;
                case ComplexityLevel.Balanced: return 20000 + scale * 1500;
                case ComplexityLevel.Optimal: return 45000 + scale * 2500;
                default: return 30000 + scale * 2000;
            }
        }

        private static ScheduleProblem BuildProblem(
            List<TimeSlot> slots, List<Auditorium> rooms, List<Teacher> teachers, List<StudentGroup> groups,
            Dictionary<int, Discipline> disciplines, List<StudyPlanItem> plan, List<TeacherPreference> prefs,
            List<Lesson> keptLessons, WeekDay[] allowedDays, AppSettings settings)
        {
            var demands = new List<Demand>();
            var occupiedByPlan = keptLessons
                .Where(l => l.StudyPlanItemId.HasValue)
                .GroupBy(l => l.StudyPlanItemId.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            var id = 0;
            foreach (var item in plan)
            {
                var already = occupiedByPlan.TryGetValue(item.Id, out var c) ? c : 0;
                var missing = Math.Max(0, item.LessonsPerWeek - already);
                var disc = disciplines[item.DisciplineId];
                for (var k = 0; k < missing; k++)
                {
                    var group = groups.First(g => g.Id == item.GroupId);
                    demands.Add(new Demand
                    {
                        Id = id++,
                        PlanItemId = item.Id,
                        DisciplineId = disc.Id,
                        DisciplineName = disc.Name,
                        DisciplineType = disc.Type,
                        DisciplineColor = disc.ColorHex,
                        TeacherId = item.TeacherId,
                        GroupIds = new[] { item.GroupId },
                        StudentCount = group.StudentCount,
                        Label = $"{disc.Name} · {group.Name} · {teachers.First(t => t.Id == item.TeacherId).FullName}"
                    });
                }
            }

            var pre = keptLessons.Select(l =>
            {
                var groupIds = l.LessonGroups != null
                    ? l.LessonGroups.Select(lg => lg.GroupId).ToArray()
                    : Array.Empty<int>();
                var studentCount = 0;
                if (groupIds.Length > 0)
                {
                    var g = groups.FirstOrDefault(x => x.Id == groupIds[0]);
                    if (g != null) studentCount = g.StudentCount;
                }
                var disciplineType = disciplines.TryGetValue(l.DisciplineId, out var d) ? (int)d.Type : 0;
                return new PreAssignment
                {
                    DemandId = -1,
                    Day = (int)l.Day,
                    SlotId = l.TimeSlotId,
                    RoomId = l.AuditoriumId,
                    IsLocked = l.IsLocked,
                    TeacherId = l.TeacherId,
                    GroupIds = groupIds,
                    DisciplineType = disciplineType,
                    StudentCount = studentCount,
                    PlanItemId = l.StudyPlanItemId
                };
            }).ToArray();

            return new ScheduleProblem
            {
                Slots = slots.Select(s => new SlotInfo { Id = s.Id, Order = s.Order, StartMinutes = s.StartMinutes, EndMinutes = s.EndMinutes }).ToArray(),
                Rooms = rooms.Select(r => new RoomInfo { Id = r.Id, Number = r.Number, Capacity = r.Capacity, Type = (int)r.Type }).ToArray(),
                Teachers = teachers.Select(t => new TeacherInfo { Id = t.Id, FullName = t.FullName }).ToArray(),
                Groups = groups.Select(g => new GroupInfo { Id = g.Id, Name = g.Name, StudentCount = g.StudentCount }).ToArray(),
                Demands = demands.ToArray(),
                PreferencesByTeacher = prefs
                    .GroupBy(p => p.TeacherId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(p => new PreferenceInfo
                        {
                            Id = p.Id,
                            Kind = p.Kind,
                            Day = p.Day,
                            SlotId = p.TimeSlotId,
                            RoomId = p.AuditoriumId,
                            Weight = p.Weight,
                            IsHard = p.IsHard
                        }).ToList()),
                Weights = settings.ToWeights(),
                MaxPairsPerDay = settings.MaxPairsPerDay,
                PreAssignments = pre,
                AllowedDays = allowedDays
            };
        }
    }
}
