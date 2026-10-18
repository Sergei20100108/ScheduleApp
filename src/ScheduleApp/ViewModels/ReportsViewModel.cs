using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScheduleApp.Data;
using ScheduleApp.Domain;
using ScheduleApp.Domain.Entities;
using ScheduleApp.Services.Validation;

namespace ScheduleApp.ViewModels
{
    public sealed class RoomUtilizationRow
    {
        public string Room { get; set; }
        public string Type { get; set; }
        public int Capacity { get; set; }
        public int Occupied { get; set; }
        public int Total { get; set; }
        public double Percent { get; set; }
    }

    public sealed class TeacherLoadRow
    {
        public string Teacher { get; set; }
        public string Department { get; set; }
        public int LessonsPerWeek { get; set; }
        public int MaxDay { get; set; }
        public string BusiestDay { get; set; }
    }

    public sealed class GroupLoadRow
    {
        public string Group { get; set; }
        public int Course { get; set; }
        public int LessonsPerWeek { get; set; }
        public int Students { get; set; }
    }

    /// <summary>Вкладка «Отчёты»: заполненность аудиторий, загрузка преподавателей и групп, конфликты.</summary>
    public sealed class ReportsViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IValidationService _validation;
        private NamedItem<SchedulePeriod> _selectedPeriod;
        private string _summaryText = "";

        public ReportsViewModel(IServiceScopeFactory scopeFactory, IValidationService validation)
        {
            _scopeFactory = scopeFactory;
            _validation = validation;
            RefreshCommand = new RelayCommand(Refresh);
        }

        public RelayCommand RefreshCommand { get; }

        public ObservableCollection<NamedItem<SchedulePeriod>> Periods { get; } = new ObservableCollection<NamedItem<SchedulePeriod>>();
        public ObservableCollection<RoomUtilizationRow> RoomUtilization { get; } = new ObservableCollection<RoomUtilizationRow>();
        public ObservableCollection<TeacherLoadRow> TeacherLoad { get; } = new ObservableCollection<TeacherLoadRow>();
        public ObservableCollection<GroupLoadRow> GroupLoad { get; } = new ObservableCollection<GroupLoadRow>();
        public ObservableCollection<ValidationIssue> Issues { get; } = new ObservableCollection<ValidationIssue>();

        public NamedItem<SchedulePeriod> SelectedPeriod
        {
            get => _selectedPeriod;
            set { if (Set(ref _selectedPeriod, value)) Refresh(); }
        }

        public string SummaryText
        {
            get => _summaryText;
            set => Set(ref _summaryText, value);
        }

        public void LoadPeriods()
        {
            Periods.Clear();
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                foreach (var p in db.Periods.AsNoTracking().OrderBy(p => p.StartDate).ToList())
                    Periods.Add(new NamedItem<SchedulePeriod> { Value = p, Name = $"{p.Name} ({p.DurationWeeks} нед.)" });
            }
            SelectedPeriod = Periods.LastOrDefault();
        }

        public void Refresh()
        {
            if (SelectedPeriod?.Value == null) return;
            RoomUtilization.Clear();
            TeacherLoad.Clear();
            GroupLoad.Clear();
            Issues.Clear();

            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                var periodId = SelectedPeriod.Value.Id;
                var lessons = db.Lessons.AsNoTracking()
                    .Include(l => l.LessonGroups)
                    .Where(l => l.PeriodId == periodId)
                    .ToList();
                var rooms = db.Auditoriums.AsNoTracking().ToList();
                var teachers = db.Teachers.AsNoTracking().ToList();
                var groups = db.Groups.AsNoTracking().ToList();
                var slotsCount = db.TimeSlots.Count();
                var daysCount = 6;
                var totalCells = slotsCount * daysCount * SelectedPeriod.Value.DurationWeeks;

                foreach (var r in rooms.OrderBy(r => r.Number))
                {
                    var occupied = lessons.Where(l => l.AuditoriumId == r.Id)
                        .Select(l => (l.Day, l.TimeSlotId)).Distinct().Count() * SelectedPeriod.Value.DurationWeeks;
                    RoomUtilization.Add(new RoomUtilizationRow
                    {
                        Room = r.Number,
                        Type = r.Type.ToString(),
                        Capacity = r.Capacity,
                        Occupied = occupied,
                        Total = totalCells,
                        Percent = totalCells == 0 ? 0 : Math.Round(100.0 * occupied / totalCells, 1)
                    });
                }

                foreach (var t in teachers.OrderBy(t => t.FullName))
                {
                    var own = lessons.Where(l => l.TeacherId == t.Id).ToList();
                    var byDay = own.GroupBy(l => l.Day).OrderByDescending(g => g.Count()).FirstOrDefault();
                    TeacherLoad.Add(new TeacherLoadRow
                    {
                        Teacher = t.FullName,
                        Department = t.Department,
                        LessonsPerWeek = own.Count,
                        MaxDay = byDay?.Count() ?? 0,
                        BusiestDay = byDay == null ? "—" : ValidationService.DayFullName(byDay.Key)
                    });
                }

                foreach (var g in groups.OrderBy(g => g.Name))
                {
                    var count = lessons.Count(l => l.LessonGroups.Any(x => x.GroupId == g.Id));
                    GroupLoad.Add(new GroupLoadRow
                    {
                        Group = g.Name,
                        Course = g.Course,
                        LessonsPerWeek = count,
                        Students = g.StudentCount
                    });
                }
            }

            var report = _validation.ValidatePeriod(SelectedPeriod.Value.Id);
            foreach (var i in report.HardViolations.Concat(report.Warnings))
                Issues.Add(i);

            SummaryText = $"Занятий в периоде: {report.TotalLessons}\n" +
                          $"Занято ячеек (уникальных слотов): {report.OccupiedCells}\n" +
                          $"Соблюдение предпочтений: {report.QualityPercent:F1}%\n" +
                          $"Нарушений жёстких ограничений: {report.HardViolations.Count}\n" +
                          $"Предупреждений (вместимость/тип аудитории): {report.Warnings.Count}";
        }
    }
}
