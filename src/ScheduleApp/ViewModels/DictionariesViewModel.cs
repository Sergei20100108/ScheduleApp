using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScheduleApp.Data;
using ScheduleApp.Domain;
using ScheduleApp.Domain.Entities;
using ScheduleApp.Services.Dialogs;

namespace ScheduleApp.ViewModels
{
    /// <summary>Вкладка «Справочники»: CRUD по всем сущностям через единую модель вида.</summary>
    public sealed class DictionariesViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IDialogService _dialogs;
        private readonly Action _onDataChanged;

        private string _selectedArea = "Преподаватели";
        private string _statusText = "";

        public DictionariesViewModel(IServiceScopeFactory scopeFactory, IDialogService dialogs, Action onDataChanged)
        {
            _scopeFactory = scopeFactory;
            _dialogs = dialogs;
            _onDataChanged = onDataChanged;

            AddCommand = new RelayCommand(AddCurrent, () => CurrentArea != null);
            RemoveCommand = new RelayCommand(RemoveCurrent, () => CurrentArea != null && CurrentArea.SelectedItem != null);
            SaveCommand = new RelayCommand(SaveCurrent, () => CurrentArea != null);
            RefreshCommand = new RelayCommand(LoadAll);
        }

        public RelayCommand AddCommand { get; }
        public RelayCommand RemoveCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand RefreshCommand { get; }

        public string StatusText
        {
            get => _statusText;
            set => Set(ref _statusText, value);
        }

        public string SelectedArea
        {
            get => _selectedArea;
            set { if (Set(ref _selectedArea, value)) { Raise(nameof(CurrentArea)); } }
        }

        public DictionaryAreaViewModel CurrentArea =>
            _areas.TryGetValue(_selectedArea ?? "", out var a) ? a : null;

        private readonly Dictionary<string, DictionaryAreaViewModel> _areas =
            new Dictionary<string, DictionaryAreaViewModel>();

        public IReadOnlyList<string> AreaNames => _areas.Keys.ToList();

        public void LoadAll()
        {
            _areas.Clear();

            _areas["Аудитории"] = new DictionaryAreaViewModel("Аудитория", _scopeFactory,
                db => db.Auditoriums.AsNoTracking().OrderBy(r => r.Number)
                    .Select(r => new AuditoriumRow { Id = r.Id, Number = r.Number, Capacity = r.Capacity, Type = r.Type, Building = r.Building }).ToList<object>(),
                ApplyAuditoriums);
            _areas["Преподаватели"] = new DictionaryAreaViewModel("Преподаватель", _scopeFactory,
                db => db.Teachers.AsNoTracking().OrderBy(t => t.FullName)
                    .Select(t => new TeacherRow { Id = t.Id, FullName = t.FullName, Department = t.Department }).ToList<object>(),
                ApplyTeachers);
            _areas["Дисциплины"] = new DictionaryAreaViewModel("Дисциплина", _scopeFactory,
                db => db.Disciplines.AsNoTracking().OrderBy(d => d.Name)
                    .Select(d => new DisciplineRow { Id = d.Id, Name = d.Name, Type = d.Type, ColorHex = d.ColorHex }).ToList<object>(),
                ApplyDisciplines);
            _areas["Учебные группы"] = new DictionaryAreaViewModel("Учебная группа", _scopeFactory,
                db => db.Groups.AsNoTracking().OrderBy(g => g.Name)
                    .Select(g => new GroupRow { Id = g.Id, Name = g.Name, Course = g.Course, StudentCount = g.StudentCount }).ToList<object>(),
                ApplyGroups);
            _areas["Учебный план"] = new DictionaryAreaViewModel("Запись плана", _scopeFactory,
                db => db.StudyPlanItems.AsNoTracking()
                    .Include(p => p.Discipline).Include(p => p.Teacher).Include(p => p.Group)
                    .OrderBy(p => p.Group.Name).ThenBy(p => p.Discipline.Name)
                    .Select(p => new PlanRow
                    {
                        Id = p.Id,
                        DisciplineName = p.Discipline.Name,
                        DisciplineType = p.Discipline.Type,
                        TeacherName = p.Teacher.FullName,
                        GroupName = p.Group.Name,
                        LessonsPerWeek = p.LessonsPerWeek,
                        DisciplineId = p.DisciplineId,
                        TeacherId = p.TeacherId,
                        GroupId = p.GroupId
                    }).ToList<object>(),
                ApplyPlan);
            _areas["Временные слоты"] = new DictionaryAreaViewModel("Слот", _scopeFactory,
                db => db.TimeSlots.AsNoTracking().OrderBy(s => s.Order)
                    .Select(s => new SlotRow { Id = s.Id, Order = s.Order, StartMinutes = s.StartMinutes, EndMinutes = s.EndMinutes }).ToList<object>(),
                ApplySlots);
            _areas["Периоды расписания"] = new DictionaryAreaViewModel("Период", _scopeFactory,
                db => db.Periods.AsNoTracking().OrderBy(p => p.StartDate)
                    .Select(p => new PeriodRow { Id = p.Id, Name = p.Name, Type = p.Type, StartDate = p.StartDate, DurationWeeks = p.DurationWeeks }).ToList<object>(),
                ApplyPeriods);
            Raise(nameof(AreaNames));
            Raise(nameof(CurrentArea));
            StatusText = "Справочники загружены";
        }

        private void AddCurrent()
        {
            CurrentArea?.AddRow();
        }

        private void RemoveCurrent()
        {
            var area = CurrentArea;
            if (area?.SelectedItem == null) return;
            var name = area.SelectedItem is IDisplayRow d ? d.Display : area.SelectedItem.ToString();
            if (!_dialogs.Confirm($"Удалить запись «{name}»?", "Удаление"))
                return;
            area.Rows.Remove(area.SelectedItem);
            area.SelectedItem = null;
        }

        private void SaveCurrent()
        {
            var area = CurrentArea;
            if (area == null) return;
            try
            {
                area.Apply();
                _onDataChanged?.Invoke();
                StatusText = $"«{SelectedArea}»: изменения сохранены";
            }
            catch (Exception ex)
            {
                _dialogs.Error($"Не удалось сохранить: {ex.Message}");
            }
        }

        private static void ApplyAuditoriums(ScheduleDbContext db, List<object> rows)
        {
            var typed = rows.OfType<AuditoriumRow>().ToList();
            var existing = db.Auditoriums.ToList();
            foreach (var row in typed)
            {
                if (string.IsNullOrWhiteSpace(row.Number)) continue;
                var e = existing.FirstOrDefault(x => x.Id == row.Id);
                if (e == null) db.Auditoriums.Add(new Auditorium { Number = row.Number, Capacity = row.Capacity, Type = row.Type, Building = row.Building });
                else { e.Number = row.Number; e.Capacity = row.Capacity; e.Type = row.Type; e.Building = row.Building; }
            }
            db.SaveChanges();
        }

        private static void ApplyTeachers(ScheduleDbContext db, List<object> rows)
        {
            var typed = rows.OfType<TeacherRow>().ToList();
            var existing = db.Teachers.ToList();
            foreach (var row in typed)
            {
                if (string.IsNullOrWhiteSpace(row.FullName)) continue;
                var e = existing.FirstOrDefault(x => x.Id == row.Id);
                if (e == null) db.Teachers.Add(new Teacher { FullName = row.FullName, Department = row.Department });
                else { e.FullName = row.FullName; e.Department = row.Department; }
            }
            db.SaveChanges();
        }

        private static void ApplyDisciplines(ScheduleDbContext db, List<object> rows)
        {
            var typed = rows.OfType<DisciplineRow>().ToList();
            var existing = db.Disciplines.ToList();
            foreach (var row in typed)
            {
                if (string.IsNullOrWhiteSpace(row.Name)) continue;
                var e = existing.FirstOrDefault(x => x.Id == row.Id);
                if (e == null) db.Disciplines.Add(new Discipline { Name = row.Name, Type = row.Type, ColorHex = row.ColorHex });
                else { e.Name = row.Name; e.Type = row.Type; e.ColorHex = row.ColorHex; }
            }
            db.SaveChanges();
        }

        private static void ApplyGroups(ScheduleDbContext db, List<object> rows)
        {
            var typed = rows.OfType<GroupRow>().ToList();
            var existing = db.Groups.ToList();
            foreach (var row in typed)
            {
                if (string.IsNullOrWhiteSpace(row.Name)) continue;
                var e = existing.FirstOrDefault(x => x.Id == row.Id);
                if (e == null) db.Groups.Add(new StudentGroup { Name = row.Name, Course = row.Course, StudentCount = row.StudentCount });
                else { e.Name = row.Name; e.Course = row.Course; e.StudentCount = row.StudentCount; }
            }
            db.SaveChanges();
        }

        private static void ApplyPlan(ScheduleDbContext db, List<object> rows)
        {
            var typed = rows.OfType<PlanRow>().ToList();
            var existing = db.StudyPlanItems.ToList();
            foreach (var row in typed)
            {
                if (row.DisciplineId == 0 || row.TeacherId == 0 || row.GroupId == 0) continue;
                var e = existing.FirstOrDefault(x => x.Id == row.Id);
                if (e == null)
                    db.StudyPlanItems.Add(new StudyPlanItem
                    {
                        DisciplineId = row.DisciplineId, TeacherId = row.TeacherId,
                        GroupId = row.GroupId, LessonsPerWeek = Math.Max(1, row.LessonsPerWeek)
                    });
                else
                {
                    e.DisciplineId = row.DisciplineId; e.TeacherId = row.TeacherId;
                    e.GroupId = row.GroupId; e.LessonsPerWeek = Math.Max(1, row.LessonsPerWeek);
                }
            }
            db.SaveChanges();
        }

        private static void ApplySlots(ScheduleDbContext db, List<object> rows)
        {
            var typed = rows.OfType<SlotRow>().ToList();
            var existing = db.TimeSlots.ToList();
            foreach (var row in typed)
            {
                var e = existing.FirstOrDefault(x => x.Id == row.Id);
                if (e == null) db.TimeSlots.Add(new TimeSlot { Order = row.Order, StartMinutes = row.StartMinutes, EndMinutes = row.EndMinutes });
                else { e.Order = row.Order; e.StartMinutes = row.StartMinutes; e.EndMinutes = row.EndMinutes; }
            }
            db.SaveChanges();
        }

        private static void ApplyPeriods(ScheduleDbContext db, List<object> rows)
        {
            var typed = rows.OfType<PeriodRow>().ToList();
            var existing = db.Periods.ToList();
            foreach (var row in typed)
            {
                if (string.IsNullOrWhiteSpace(row.Name)) continue;
                var e = existing.FirstOrDefault(x => x.Id == row.Id);
                if (e == null) db.Periods.Add(new SchedulePeriod { Name = row.Name, Type = row.Type, StartDate = row.StartDate, DurationWeeks = Math.Max(1, row.DurationWeeks) });
                else { e.Name = row.Name; e.Type = row.Type; e.StartDate = row.StartDate; e.DurationWeeks = Math.Max(1, row.DurationWeeks); }
            }
            db.SaveChanges();
        }
    }

    /// <summary>Строки-модели справочников отображаются по-единому через интерфейс.</summary>
    public interface IDisplayRow
    {
        string Display { get; }
    }

    public sealed class DictionaryAreaViewModel : ObservableObject
    {
        private object _selectedItem;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly Func<ScheduleDbContext, List<object>> _load;
        private readonly Action<ScheduleDbContext, List<object>> _apply;
        private readonly string _entityName;

        public DictionaryAreaViewModel(string entityName, IServiceScopeFactory scopeFactory,
            Func<ScheduleDbContext, List<object>> load, Action<ScheduleDbContext, List<object>> apply)
        {
            _entityName = entityName;
            _scopeFactory = scopeFactory;
            _load = load;
            _apply = apply;
            Rows = new ObservableCollection<object>(LoadRows());
        }

        private List<object> LoadRows()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                return _load(db);
            }
        }

        public string EntityName => _entityName;
        public ObservableCollection<object> Rows { get; }

        public object SelectedItem
        {
            get => _selectedItem;
            set => Set(ref _selectedItem, value);
        }

        public void AddRow()
        {
            object row;
            switch (_entityName)
            {
                case "Аудитория": row = new AuditoriumRow { Capacity = 30, Type = AuditoriumType.Laboratory }; break;
                case "Преподаватель": row = new TeacherRow { Department = "Кафедра" }; break;
                case "Дисциплина": row = new DisciplineRow { Type = DisciplineType.Lecture, ColorHex = "#4E79A7" }; break;
                case "Учебная группа": row = new GroupRow { Course = 1, StudentCount = 25 }; break;
                case "Запись плана": row = new PlanRow { LessonsPerWeek = 1 }; break;
                case "Слот": row = new SlotRow { Order = Rows.Count + 1, StartMinutes = 480, EndMinutes = 570 }; break;
                default: row = new PeriodRow { Type = PeriodType.Week, StartDate = DateTime.Today, DurationWeeks = 16 }; break;
            }
            Rows.Add(row);
            SelectedItem = row;
        }

        public void Apply()
        {
            var typed = new List<object>(Rows);
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                _apply(db, typed);
            }
            Rows.Clear();
            foreach (var r in LoadRows()) Rows.Add(r);
        }
    }

    public sealed class AuditoriumRow : IDisplayRow
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int Capacity { get; set; }
        public AuditoriumType Type { get; set; }
        public string Building { get; set; }
        public string Display => Number;
    }

    public sealed class TeacherRow : IDisplayRow
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string Department { get; set; }
        public string Display => FullName;
    }

    public sealed class DisciplineRow : IDisplayRow
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public DisciplineType Type { get; set; }
        public string ColorHex { get; set; }
        public string Display => Name;
    }

    public sealed class GroupRow : IDisplayRow
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Course { get; set; }
        public int StudentCount { get; set; }
        public string Display => Name;
    }

    public sealed class PlanRow : IDisplayRow
    {
        public int Id { get; set; }
        public int DisciplineId { get; set; }
        public int TeacherId { get; set; }
        public int GroupId { get; set; }
        public string DisciplineName { get; set; }
        public DisciplineType DisciplineType { get; set; }
        public string TeacherName { get; set; }
        public string GroupName { get; set; }
        public int LessonsPerWeek { get; set; }
        public string Display => $"{DisciplineName} — {GroupName}";
    }

    public sealed class SlotRow : IDisplayRow
    {
        public int Id { get; set; }
        public int Order { get; set; }
        public int StartMinutes { get; set; }
        public int EndMinutes { get; set; }
        public string Display => $"{TimeSlot.MinutesToString(StartMinutes)} – {TimeSlot.MinutesToString(EndMinutes)}";
    }

    public sealed class PeriodRow : IDisplayRow
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public PeriodType Type { get; set; }
        public DateTime StartDate { get; set; }
        public int DurationWeeks { get; set; }
        public string Display => Name;
    }
}
