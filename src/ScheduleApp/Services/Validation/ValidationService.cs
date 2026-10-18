using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScheduleApp.Data;
using ScheduleApp.Domain;
using ScheduleApp.Domain.Entities;
using ScheduleApp.Services.Settings;

namespace ScheduleApp.Services.Validation
{
    public sealed class ValidationIssue
    {
        public string Severity;      // "Ошибка" | "Предупреждение"
        public string Description;
        public int LessonId;
        public string Context;
    }

    public sealed class ValidationReport
    {
        public List<ValidationIssue> HardViolations = new List<ValidationIssue>();
        public List<ValidationIssue> Warnings = new List<ValidationIssue>();
        public HashSet<int> ConflictLessonIds = new HashSet<int>();
        public double QualityPercent;
        public int TotalLessons;
        public int OccupiedCells;
        public double SatisfiedWeight;
        public double TotalWeight;
    }

    public interface IValidationService
    {
        ValidationReport ValidatePeriod(int periodId);
    }

    public sealed class ValidationService : IValidationService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISettingsService _settings;

        public ValidationService(IServiceScopeFactory scopeFactory, ISettingsService settings)
        {
            _scopeFactory = scopeFactory;
            _settings = settings;
        }

        public ValidationReport ValidatePeriod(int periodId)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                var lessons = db.Lessons.AsNoTracking()
                    .Include(l => l.LessonGroups)
                    .Where(l => l.PeriodId == periodId)
                    .ToList();
                var prefs = db.TeacherPreferences.AsNoTracking().ToList();
                var slots = db.TimeSlots.AsNoTracking().ToDictionary(s => s.Id);
                var rooms = db.Auditoriums.AsNoTracking().ToDictionary(r => r.Id);
                var groups = db.Groups.AsNoTracking().ToDictionary(g => g.Id);
                var teachers = db.Teachers.AsNoTracking().ToDictionary(t => t.Id);
                var disciplines = db.Disciplines.AsNoTracking().ToDictionary(d => d.Id);

                var report = new ValidationReport { TotalLessons = lessons.Count };
                var settings = _settings.Current;

                void Conflict(int a, int b, string reason)
                {
                    report.HardViolations.Add(new ValidationIssue
                    {
                        Severity = "Ошибка",
                        Description = reason,
                        LessonId = a,
                        Context = ContextOf(a)
                    });
                    report.ConflictLessonIds.Add(a);
                    report.ConflictLessonIds.Add(b);
                }

                string ContextOf(int lessonId)
                {
                    var l = lessons.FirstOrDefault(x => x.Id == lessonId);
                    if (l == null) return "";
                    var g = string.Join(", ", l.LessonGroups.Select(x => groups[x.GroupId].Name));
                    var discName = disciplines.TryGetValue(l.DisciplineId, out var disc) ? disc.Name : $"#{l.DisciplineId}";
                    return $"{discName} · {teachers[l.TeacherId].FullName} · ауд. {rooms[l.AuditoriumId].Number} · {g} · {DayName(l.Day)} {slots[l.TimeSlotId].Display}";
                }

                // 1. Одновременная занятость преподавателя / аудитории / группы
                var byTeacherCell = lessons.GroupBy(l => new { l.TeacherId, l.Day, l.TimeSlotId });
                foreach (var g in byTeacherCell.Where(x => x.Count() > 1))
                {
                    var ids = g.Select(l => l.Id).ToList();
                    for (var i = 1; i < ids.Count; i++)
                        Conflict(ids[0], ids[i],
                            $"Преподаватель {teachers[g.Key.TeacherId].FullName} ведёт два занятия одновременно ({DayName(g.Key.Day)}, {slots[g.Key.TimeSlotId].Display})");
                }

                var byRoomCell = lessons.GroupBy(l => new { l.AuditoriumId, l.Day, l.TimeSlotId });
                foreach (var g in byRoomCell.Where(x => x.Count() > 1))
                {
                    var ids = g.Select(l => l.Id).ToList();
                    for (var i = 1; i < ids.Count; i++)
                        Conflict(ids[0], ids[i],
                            $"Аудитория {rooms[g.Key.AuditoriumId].Number} занята двумя группами ({DayName(g.Key.Day)}, {slots[g.Key.TimeSlotId].Display})");
                }

                var byGroupCell = lessons
                    .SelectMany(l => l.LessonGroups.Select(lg => new { lg.GroupId, l.Day, l.TimeSlotId, LessonId = l.Id }))
                    .GroupBy(x => new { x.GroupId, x.Day, x.TimeSlotId });
                foreach (var g in byGroupCell.Where(x => x.Count() > 1))
                {
                    var ids = g.Select(x => x.LessonId).ToList();
                    for (var i = 1; i < ids.Count; i++)
                        Conflict(ids[0], ids[i],
                            $"Группа {groups[g.Key.GroupId].Name} имеет два занятия одновременно ({DayName(g.Key.Day)}, {slots[g.Key.TimeSlotId].Display})");
                }

                // 2. Жёсткие предпочтения преподавателей
                var prefsByTeacher = prefs.Where(p => p.IsHard).GroupBy(p => p.TeacherId)
                    .ToDictionary(g => g.Key, g => g.ToList());
                foreach (var l in lessons)
                {
                    if (!prefsByTeacher.TryGetValue(l.TeacherId, out var list)) continue;
                    foreach (var p in list)
                    {
                        var violated = p.Kind switch
                        {
                            PreferenceKind.ForbiddenDay => p.Day == l.Day,
                            PreferenceKind.ForbiddenTime => p.TimeSlotId == l.TimeSlotId,
                            PreferenceKind.PreferredDay => p.Day.HasValue && p.Day.Value != l.Day,
                            PreferenceKind.PreferredTime => p.TimeSlotId.HasValue && p.TimeSlotId.Value != l.TimeSlotId,
                            PreferenceKind.PreferredAuditorium => p.AuditoriumId.HasValue && p.AuditoriumId.Value != l.AuditoriumId,
                            PreferenceKind.AvoidAuditorium => p.AuditoriumId == l.AuditoriumId,
                            _ => false
                        };
                        if (violated)
                        {
                            report.HardViolations.Add(new ValidationIssue
                            {
                                Severity = "Ошибка",
                                Description = $"Нарушен жёсткий запрет преподавателя {teachers[l.TeacherId].FullName}: {PrefText(p)}",
                                LessonId = l.Id,
                                Context = ContextOf(l.Id)
                            });
                            report.ConflictLessonIds.Add(l.Id);
                        }
                    }
                }

                // 3. Мягкие предупреждения: вместимость, тип аудитории
                foreach (var l in lessons)
                {
                    var room = rooms[l.AuditoriumId];
                    var maxStudents = l.LessonGroups.Count == 0 ? 0 : l.LessonGroups.Max(x => groups[x.GroupId].StudentCount);
                    if (room.Capacity < maxStudents)
                    {
                        report.Warnings.Add(new ValidationIssue
                        {
                            Severity = "Предупреждение",
                            Description = $"Аудитория {room.Number} ({room.Capacity} мест) меньше потока ({maxStudents} студентов)",
                            LessonId = l.Id,
                            Context = ContextOf(l.Id)
                        });
                    }
                    var disc = disciplines[l.DisciplineId];
                    if (!ConstraintEngineShim.RoomMatches(disc.Type, room.Type))
                    {
                        report.Warnings.Add(new ValidationIssue
                        {
                            Severity = "Предупреждение",
                            Description = $"Тип аудитории {room.Number} ({TypeName(room.Type)}) не соответствует типу дисциплины ({TypeNameDisc(disc.Type)})",
                            LessonId = l.Id,
                            Context = ContextOf(l.Id)
                        });
                    }
                }

                // 4. Качество: сумма весов соблюдённых мягких предпочтений
                report.OccupiedCells = lessons
                    .Select(l => (l.Day, l.TimeSlotId))
                    .Distinct().Count();
                ComputeQuality(report, lessons, prefs, settings, slots.Keys.ToArray());
                return report;
            }
        }

        private static void ComputeQuality(ValidationReport report, List<Lesson> lessons,
            List<TeacherPreference> prefs, Settings.AppSettings settings, int[] slotIds)
        {
            var byTeacher = prefs.Where(p => !p.IsHard).GroupBy(p => p.TeacherId)
                .ToDictionary(g => g.Key, g => g.ToList());

            double total = 0, satisfied = 0;
            foreach (var l in lessons)
            {
                if (!byTeacher.TryGetValue(l.TeacherId, out var list)) continue;
                foreach (var p in list)
                {
                    var w = p.Weight / 100.0;
                    var matched = p.Kind switch
                    {
                        PreferenceKind.ForbiddenDay => p.Day == l.Day,
                        PreferenceKind.ForbiddenTime => p.TimeSlotId == l.TimeSlotId,
                        PreferenceKind.PreferredDay => p.Day == l.Day,
                        PreferenceKind.PreferredTime => p.TimeSlotId == l.TimeSlotId,
                        PreferenceKind.PreferredAuditorium => p.AuditoriumId == l.AuditoriumId,
                        PreferenceKind.AvoidAuditorium => p.AuditoriumId == l.AuditoriumId,
                        _ => false
                    };
                    var isPositive = p.Kind == PreferenceKind.PreferredDay || p.Kind == PreferenceKind.PreferredTime ||
                                     p.Kind == PreferenceKind.PreferredAuditorium;
                    total += w;
                    if (matched == isPositive) satisfied += w;
                    else if (matched && !isPositive) { /* нарушено: уже не добавляем */ }
                }
            }
            report.TotalWeight = total;
            report.SatisfiedWeight = satisfied;
            report.QualityPercent = total <= 0 ? 100 : Math.Round(100.0 * satisfied / total, 1);
        }

        private static string PrefText(TeacherPreference p) => p.Kind switch
        {
            PreferenceKind.ForbiddenDay => $"нельзя {DayName(p.Day.Value)}",
            PreferenceKind.ForbiddenTime => "нельзя в это время",
            PreferenceKind.PreferredDay => $"только {DayName(p.Day.Value)}",
            PreferenceKind.PreferredTime => "только в это время",
            PreferenceKind.PreferredAuditorium => "только в своей аудитории",
            PreferenceKind.AvoidAuditorium => "аудитория нежелательна",
            _ => p.Kind.ToString()
        };

        internal static string DayName(WeekDay d) => d switch
        {
            WeekDay.Monday => "Пн", WeekDay.Tuesday => "Вт", WeekDay.Wednesday => "Ср",
            WeekDay.Thursday => "Чт", WeekDay.Friday => "Пт", WeekDay.Saturday => "Сб",
            _ => "Вс"
        };

        internal static string DayFullName(WeekDay d) => d switch
        {
            WeekDay.Monday => "Понедельник", WeekDay.Tuesday => "Вторник", WeekDay.Wednesday => "Среда",
            WeekDay.Thursday => "Четверг", WeekDay.Friday => "Пятница", WeekDay.Saturday => "Суббота",
            _ => "Воскресенье"
        };

        internal static string TypeName(AuditoriumType t) => t switch
        {
            AuditoriumType.Lecture => "Лекционная", AuditoriumType.Laboratory => "Лаборатория",
            AuditoriumType.ComputerClass => "Компьютерный класс", AuditoriumType.Seminar => "Семинарская",
            AuditoriumType.Gym => "Спортивный зал", _ => "Мастерская"
        };

        internal static string TypeNameDisc(DisciplineType t) => t switch
        {
            DisciplineType.Lecture => "Лекция", DisciplineType.Practice => "Практика",
            DisciplineType.Laboratory => "Лабораторная", DisciplineType.Seminar => "Семинар",
            _ => "Физвоспитание"
        };
    }

    /// <summary>Обёртка над общим правилом совместимости типов (чтобы не тянуть ядро планировщика в UI-сервисы).</summary>
    internal static class ConstraintEngineShim
    {
        public static bool RoomMatches(DisciplineType d, AuditoriumType r) =>
            Scheduling.ConstraintEngine.RoomMatches(d, r);
    }
}
