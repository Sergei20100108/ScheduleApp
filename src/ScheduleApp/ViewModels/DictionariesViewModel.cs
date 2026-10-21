using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
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
            RemoveCommand = new RelayCommand(RemoveCurrent, () => CurrentArea?.SelectedItem != null);
            SaveCommand = new RelayCommand(SaveCurrent, () => CurrentArea != null);
            RefreshCommand = new RelayCommand(LoadAll);
        }

        /// <summary>Строка добавлена в таблицу — нужно перевести фокус в её ячейку.</summary>
        public event EventHandler RowAdded;

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
            set
            {
                if (Set(ref _selectedArea, value))
                {
                    Raise(nameof(CurrentArea));
                    Raise(nameof(SelectedRow));
                }
            }
        }

        /// <summary>
        /// Выбранная строка текущего раздела.
        /// </summary>
        public object SelectedRow
        {
            get => CurrentArea?.SelectedItem;
            set
            {
                var area = CurrentArea;
                if (area == null) return;
                if (!ReferenceEquals(area.SelectedItem, value))
                    area.SelectedItem = value;
                else
                    Raise();
            }
        }

        private void OnAreaPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DictionaryAreaViewModel.SelectedItem)) return;
            Raise(nameof(SelectedRow));
            foreach (var pair in _areas)
            {
                if (ReferenceEquals(pair.Value, sender))
                {
                    Raise(SelectedPropertyName(pair.Key));
                    break;
                }
            }
        }

        private static string SelectedPropertyName(string areaName)
        {
            switch (areaName)
            {
                case "Аудитории": return nameof(SelectedAuditorium);
                case "Преподаватели": return nameof(SelectedTeacher);
                case "Дисциплины": return nameof(SelectedDiscipline);
                case "Учебные группы": return nameof(SelectedGroup);
                case "Учебный план": return nameof(SelectedPlanItem);
                case "Временные слоты": return nameof(SelectedSlot);
                case "Периоды расписания": return nameof(SelectedPeriod);
                default: return nameof(SelectedRow);
            }
        }

        private void RaiseAreaProperties()
        {
            Raise(nameof(Auditoriums));
            Raise(nameof(SelectedAuditorium));
            Raise(nameof(Teachers));
            Raise(nameof(SelectedTeacher));
            Raise(nameof(Disciplines));
            Raise(nameof(SelectedDiscipline));
            Raise(nameof(Groups));
            Raise(nameof(SelectedGroup));
            Raise(nameof(PlanItems));
            Raise(nameof(SelectedPlanItem));
            Raise(nameof(Slots));
            Raise(nameof(SelectedSlot));
            Raise(nameof(Periods));
            Raise(nameof(SelectedPeriod));
        }

        public DictionaryAreaViewModel CurrentArea =>
            _areas.TryGetValue(_selectedArea ?? "", out var a) ? a : null;

        private readonly Dictionary<string, DictionaryAreaViewModel> _areas =
            new Dictionary<string, DictionaryAreaViewModel>();

        public IReadOnlyList<string> AreaNames => _areas.Keys.ToList();

        private DictionaryAreaViewModel Area(string name) =>
            _areas.TryGetValue(name ?? "", out var area) ? area : null;

        /// <summary>Строки конкретного раздела: каждой таблице нужен свой список,
        /// иначе колонки получают чужие типы строк и привязки перестают разрешаться.</summary>
        public ObservableCollection<object> Auditoriums => Area("Аудитории")?.Rows;
        public ObservableCollection<object> Teachers => Area("Преподаватели")?.Rows;
        public ObservableCollection<object> Disciplines => Area("Дисциплины")?.Rows;
        public ObservableCollection<object> Groups => Area("Учебные группы")?.Rows;
        public ObservableCollection<object> PlanItems => Area("Учебный план")?.Rows;
        public ObservableCollection<object> Slots => Area("Временные слоты")?.Rows;
        public ObservableCollection<object> Periods => Area("Периоды расписания")?.Rows;

        public object SelectedAuditorium { get => Area("Аудитории")?.SelectedItem; set => SetAreaItem("Аудитории", value); }
        public object SelectedTeacher { get => Area("Преподаватели")?.SelectedItem; set => SetAreaItem("Преподаватели", value); }
        public object SelectedDiscipline { get => Area("Дисциплины")?.SelectedItem; set => SetAreaItem("Дисциплины", value); }
        public object SelectedGroup { get => Area("Учебные группы")?.SelectedItem; set => SetAreaItem("Учебные группы", value); }
        public object SelectedPlanItem { get => Area("Учебный план")?.SelectedItem; set => SetAreaItem("Учебный план", value); }
        public object SelectedSlot { get => Area("Временные слоты")?.SelectedItem; set => SetAreaItem("Временные слоты", value); }
        public object SelectedPeriod { get => Area("Периоды расписания")?.SelectedItem; set => SetAreaItem("Периоды расписания", value); }

        private void SetAreaItem(string areaName, object value)
        {
            var area = Area(areaName);
            if (area == null) return;
            if (!ReferenceEquals(area.SelectedItem, value))
                area.SelectedItem = value;
        }

        public void LoadAll()
        {
            if (_areas.Count == 0)
            {
                _areas["Аудитории"] = new DictionaryAreaViewModel("Аудитория", _scopeFactory,
                    db => db.Auditoriums.AsNoTracking().OrderBy(r => r.Number)
                        .Select(r => new AuditoriumRow { Id = r.Id, Number = r.Number, Capacity = r.Capacity, Type = r.Type, Building = r.Building }).ToList<object>(),
                    ApplyAuditoriums, RemoveAuditorium);
                _areas["Преподаватели"] = new DictionaryAreaViewModel("Преподаватель", _scopeFactory,
                    db => db.Teachers.AsNoTracking().OrderBy(t => t.FullName)
                        .Select(t => new TeacherRow { Id = t.Id, FullName = t.FullName, Department = t.Department }).ToList<object>(),
                    ApplyTeachers, RemoveTeacher);
                _areas["Дисциплины"] = new DictionaryAreaViewModel("Дисциплина", _scopeFactory,
                    db => db.Disciplines.AsNoTracking().OrderBy(d => d.Name)
                        .Select(d => new DisciplineRow { Id = d.Id, Name = d.Name, Type = d.Type, ColorHex = d.ColorHex }).ToList<object>(),
                    ApplyDisciplines, RemoveDiscipline);
                _areas["Учебные группы"] = new DictionaryAreaViewModel("Учебная группа", _scopeFactory,
                    db => db.Groups.AsNoTracking().OrderBy(g => g.Name)
                        .Select(g => new GroupRow { Id = g.Id, Name = g.Name, Course = g.Course, StudentCount = g.StudentCount }).ToList<object>(),
                    ApplyGroups, RemoveGroup);
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
                    ApplyPlan, RemovePlanItem);
                _areas["Временные слоты"] = new DictionaryAreaViewModel("Слот", _scopeFactory,
                    db => db.TimeSlots.AsNoTracking().OrderBy(s => s.Order)
                        .Select(s => new SlotRow { Id = s.Id, Order = s.Order, StartMinutes = s.StartMinutes, EndMinutes = s.EndMinutes }).ToList<object>(),
                    ApplySlots, RemoveSlot);
                _areas["Периоды расписания"] = new DictionaryAreaViewModel("Период", _scopeFactory,
                    db => db.Periods.AsNoTracking().OrderBy(p => p.StartDate)
                        .Select(p => new PeriodRow { Id = p.Id, Name = p.Name, Type = p.Type, StartDate = p.StartDate, DurationWeeks = p.DurationWeeks }).ToList<object>(),
                    ApplyPeriods, RemovePeriod);
                foreach (var area in _areas.Values) area.PropertyChanged += OnAreaPropertyChanged;
            }
            else
            {
                foreach (var area in _areas.Values) area.Reload();
            }

            Raise(nameof(AreaNames));
            Raise(nameof(CurrentArea));
            Raise(nameof(SelectedRow));
            RaiseAreaProperties();
            StatusText = "Справочники загружены";
        }

        private void AddCurrent()
        {
            var area = CurrentArea;
            if (area == null) return;
            area.AddRow();
            StatusText = $"«{SelectedArea}»: добавлена строка — заполните её и сохраните";
            RowAdded?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Удаляет выбранную запись из базы данных (а не только из таблицы).</summary>
        private void RemoveCurrent()
        {
            var area = CurrentArea;
            var row = area?.SelectedItem;
            if (area == null || row == null) return;

            var name = row is IDisplayRow d ? d.Display : row.ToString();
            if (string.IsNullOrWhiteSpace(name)) name = area.EntityName;

            if (!_dialogs.Confirm($"Удалить «{name}» из базы данных?", "Удаление"))
                return;

            try
            {
                area.RemoveSelected();
                _onDataChanged?.Invoke();
                StatusText = $"«{SelectedArea}»: «{name}» удалён";
            }
            catch (OperationCanceledException)
            {
                StatusText = $"«{SelectedArea}»: удаление отменено";
            }
            catch (Exception ex)
            {
                _dialogs.Error(ex.Message);
                StatusText = $"«{SelectedArea}»: не удалось удалить «{name}»";
            }
        }

        private void SaveCurrent()
        {
            var area = CurrentArea;
            if (area == null) return;

            var problem = area.Validate();
            if (problem != null)
            {
                _dialogs.Warn(problem);
                StatusText = $"«{SelectedArea}»: проверьте данные";
                return;
            }

            try
            {
                area.Apply();
                _onDataChanged?.Invoke();
                StatusText = $"«{SelectedArea}»: изменения сохранены";
            }
            catch (DbUpdateException ex)
            {
                _dialogs.Error(FriendlySaveError(ex));
                StatusText = $"«{SelectedArea}»: изменения не сохранены";
            }
            catch (Exception ex)
            {
                _dialogs.Error($"Не удалось сохранить: {ex.Message}");
                StatusText = $"«{SelectedArea}»: изменения не сохранены";
            }
        }

        private static string FriendlySaveError(DbUpdateException ex)
        {
            var details = ex.InnerException?.Message ?? ex.Message;
            if (details.IndexOf("UNIQUE", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Не удалось сохранить: такая запись уже существует. Проверьте название.\n\n" + details;
            return "Не удалось сохранить: нарушено ограничение целостности данных.\n\n" + details;
        }

        private static void ApplyAuditoriums(ScheduleDbContext db, List<object> rows)
        {
            var typed = rows.OfType<AuditoriumRow>().ToList();
            var existing = db.Auditoriums.ToList();
            foreach (var row in typed)
            {
                if (string.IsNullOrWhiteSpace(row.Number)) continue;
                var e = existing.FirstOrDefault(x => x.Id == row.Id);
                if (e == null)
                {
                    if (row.Id != 0) continue;
                    db.Auditoriums.Add(new Auditorium { Number = row.Number, Capacity = row.Capacity, Type = row.Type, Building = row.Building });
                }
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
                if (e == null)
                {
                    if (row.Id != 0) continue;
                    db.Teachers.Add(new Teacher { FullName = row.FullName, Department = row.Department });
                }
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
                if (e == null)
                {
                    if (row.Id != 0) continue;
                    db.Disciplines.Add(new Discipline { Name = row.Name, Type = row.Type, ColorHex = row.ColorHex });
                }
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
                if (e == null)
                {
                    if (row.Id != 0) continue;
                    db.Groups.Add(new StudentGroup { Name = row.Name, Course = row.Course, StudentCount = row.StudentCount });
                }
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
                {
                    if (row.Id != 0) continue;
                    db.StudyPlanItems.Add(new StudyPlanItem
                    {
                        DisciplineId = row.DisciplineId, TeacherId = row.TeacherId,
                        GroupId = row.GroupId, LessonsPerWeek = Math.Max(1, row.LessonsPerWeek)
                    });
                }
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
                if (e == null)
                {
                    if (row.Id != 0) continue;
                    db.TimeSlots.Add(new TimeSlot { Order = row.Order, StartMinutes = row.StartMinutes, EndMinutes = row.EndMinutes });
                }
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
                if (e == null)
                {
                    if (row.Id != 0) continue;
                    db.Periods.Add(new SchedulePeriod { Name = row.Name, Type = row.Type, StartDate = row.StartDate, DurationWeeks = Math.Max(1, row.DurationWeeks) });
                }
                else { e.Name = row.Name; e.Type = row.Type; e.StartDate = row.StartDate; e.DurationWeeks = Math.Max(1, row.DurationWeeks); }
            }
            db.SaveChanges();
        }

        // ==================== Удаление записей ====================

        /// <summary>Удаляет преподавателя вместе со связанными записями (после подтверждения).</summary>
        private void RemoveTeacher(ScheduleDbContext db, object row)
        {
            var id = ((TeacherRow)row).Id;
            var teacher = db.Teachers.FirstOrDefault(t => t.Id == id);
            if (teacher == null) return;

            var planCount = db.StudyPlanItems.Count(p => p.TeacherId == id);
            var lessonCount = db.Lessons.Count(l => l.TeacherId == id);
            var prefCount = db.TeacherPreferences.Count(p => p.TeacherId == id);

            if (planCount + lessonCount + prefCount > 0)
            {
                var message =
                    $"Преподаватель «{teacher.FullName}» связан с другими данными:\n" +
                    $"• учебный план — {planCount} записей;\n" +
                    $"• занятия — {lessonCount};\n" +
                    $"• предпочтения — {prefCount}.\n\n" +
                    "Удалить преподавателя вместе со связанными записями?";
                if (!_dialogs.Confirm(message, "Удаление преподавателя"))
                    throw new OperationCanceledException("Удаление отменено пользователем");
            }

            if (lessonCount > 0)
                db.Lessons.RemoveRange(db.Lessons.Where(l => l.TeacherId == id).ToList());
            if (planCount > 0)
                db.StudyPlanItems.RemoveRange(db.StudyPlanItems.Where(p => p.TeacherId == id).ToList());
            if (prefCount > 0)
                db.TeacherPreferences.RemoveRange(db.TeacherPreferences.Where(p => p.TeacherId == id).ToList());

            db.Teachers.Remove(teacher);
            db.SaveChanges();
        }

        /// <summary>Удаляет учебную группу вместе со связанными записями (после подтверждения).</summary>
        private void RemoveGroup(ScheduleDbContext db, object row)
        {
            var id = ((GroupRow)row).Id;
            var group = db.Groups.FirstOrDefault(g => g.Id == id);
            if (group == null) return;

            var planCount = db.StudyPlanItems.Count(p => p.GroupId == id);
            var linkCount = db.LessonGroups.Count(lg => lg.GroupId == id);

            if (planCount + linkCount > 0)
            {
                var message =
                    $"Группа «{group.Name}» связана с другими данными:\n" +
                    $"• учебный план — {planCount} записей;\n" +
                    $"• занятия — {linkCount}.\n\n" +
                    "Удалить группу вместе со связанными записями?";
                if (!_dialogs.Confirm(message, "Удаление группы"))
                    throw new OperationCanceledException("Удаление отменено пользователем");
            }

            if (linkCount > 0)
                db.LessonGroups.RemoveRange(db.LessonGroups.Where(lg => lg.GroupId == id).ToList());
            if (planCount > 0)
                db.StudyPlanItems.RemoveRange(db.StudyPlanItems.Where(p => p.GroupId == id).ToList());

            db.Groups.Remove(group);
            db.SaveChanges();
        }

        private void RemoveAuditorium(ScheduleDbContext db, object row)
        {
            var id = ((AuditoriumRow)row).Id;
            var entity = db.Auditoriums.FirstOrDefault(a => a.Id == id);
            if (entity == null) return;

            var lessonCount = db.Lessons.Count(l => l.AuditoriumId == id);
            var prefCount = db.TeacherPreferences.Count(p => p.AuditoriumId == id);
            if (lessonCount + prefCount > 0)
            {
                var message =
                    $"Аудитория «{entity.Number}» используется в {lessonCount} занятиях и {prefCount} предпочтениях.\n\n" +
                    "Удалить аудиторию вместе со связанными записями?";
                if (!_dialogs.Confirm(message, "Удаление аудитории"))
                    throw new OperationCanceledException("Удаление отменено пользователем");
            }

            if (lessonCount > 0)
                db.Lessons.RemoveRange(db.Lessons.Where(l => l.AuditoriumId == id).ToList());
            if (prefCount > 0)
                db.TeacherPreferences.RemoveRange(db.TeacherPreferences.Where(p => p.AuditoriumId == id).ToList());

            db.Auditoriums.Remove(entity);
            db.SaveChanges();
        }

        private void RemoveDiscipline(ScheduleDbContext db, object row)
        {
            var id = ((DisciplineRow)row).Id;
            var entity = db.Disciplines.FirstOrDefault(d => d.Id == id);
            if (entity == null) return;

            var lessonCount = db.Lessons.Count(l => l.DisciplineId == id);
            var planCount = db.StudyPlanItems.Count(p => p.DisciplineId == id);
            if (lessonCount + planCount > 0)
            {
                var message =
                    $"Дисциплина «{entity.Name}» используется в {lessonCount} занятиях и {planCount} записях учебного плана.\n\n" +
                    "Удалить дисциплину вместе со связанными записями?";
                if (!_dialogs.Confirm(message, "Удаление дисциплины"))
                    throw new OperationCanceledException("Удаление отменено пользователем");
            }

            if (lessonCount > 0)
                db.Lessons.RemoveRange(db.Lessons.Where(l => l.DisciplineId == id).ToList());
            if (planCount > 0)
                db.StudyPlanItems.RemoveRange(db.StudyPlanItems.Where(p => p.DisciplineId == id).ToList());

            db.Disciplines.Remove(entity);
            db.SaveChanges();
        }

        private void RemoveSlot(ScheduleDbContext db, object row)
        {
            var id = ((SlotRow)row).Id;
            var entity = db.TimeSlots.FirstOrDefault(s => s.Id == id);
            if (entity == null) return;

            var lessonCount = db.Lessons.Count(l => l.TimeSlotId == id);
            var prefCount = db.TeacherPreferences.Count(p => p.TimeSlotId == id);
            if (lessonCount + prefCount > 0)
            {
                var message =
                    $"Слот «{entity.Display}» используется в {lessonCount} занятиях и {prefCount} предпочтениях.\n\n" +
                    "Удалить слот вместе со связанными записями?";
                if (!_dialogs.Confirm(message, "Удаление слота"))
                    throw new OperationCanceledException("Удаление отменено пользователем");
            }

            if (lessonCount > 0)
                db.Lessons.RemoveRange(db.Lessons.Where(l => l.TimeSlotId == id).ToList());
            if (prefCount > 0)
                db.TeacherPreferences.RemoveRange(db.TeacherPreferences.Where(p => p.TimeSlotId == id).ToList());

            db.TimeSlots.Remove(entity);
            db.SaveChanges();
        }

        private void RemovePlanItem(ScheduleDbContext db, object row)
        {
            var id = ((PlanRow)row).Id;
            var entity = db.StudyPlanItems.FirstOrDefault(p => p.Id == id);
            if (entity == null) return;
            db.StudyPlanItems.Remove(entity);
            db.SaveChanges();
        }

        private void RemovePeriod(ScheduleDbContext db, object row)
        {
            var id = ((PeriodRow)row).Id;
            var entity = db.Periods.FirstOrDefault(p => p.Id == id);
            if (entity == null) return;

            var lessonCount = db.Lessons.Count(l => l.PeriodId == id);
            if (lessonCount > 0)
            {
                var message =
                    $"Период «{entity.Name}» содержит {lessonCount} занятий — они будут удалены вместе с ним.\n\n" +
                    "Продолжить?";
                if (!_dialogs.Confirm(message, "Удаление периода"))
                    throw new OperationCanceledException("Удаление отменено пользователем");
            }

            db.Periods.Remove(entity);
            db.SaveChanges();
        }
    }

    /// <summary>Строки-модели справочников отображаются по-единому через интерфейс.</summary>
    public interface IDisplayRow
    {
        string Display { get; }

        /// <summary>Идентификатор записи в базе данных (0 — новая, ещё не сохранённая строка).</summary>
        int Id { get; }
    }

    public sealed class DictionaryAreaViewModel : ObservableObject
    {
        private object _selectedItem;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly Func<ScheduleDbContext, List<object>> _load;
        private readonly Action<ScheduleDbContext, List<object>> _apply;
        private readonly Action<ScheduleDbContext, object> _remove;
        private readonly string _entityName;

        public DictionaryAreaViewModel(string entityName, IServiceScopeFactory scopeFactory,
            Func<ScheduleDbContext, List<object>> load, Action<ScheduleDbContext, List<object>> apply,
            Action<ScheduleDbContext, object> remove)
        {
            _entityName = entityName;
            _scopeFactory = scopeFactory;
            _load = load;
            _apply = apply;
            _remove = remove;
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
            set
            {
                if (Set(ref _selectedItem, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public void AddRow()
        {
            object row;
            switch (_entityName)
            {
                case "Аудитория": row = new AuditoriumRow { Number = NextRoomNumber(), Capacity = 30, Type = AuditoriumType.Laboratory }; break;
                case "Преподаватель": row = new TeacherRow { FullName = UniqueName("Новый преподаватель"), Department = "Кафедра" }; break;
                case "Дисциплина": row = new DisciplineRow { Name = UniqueName("Новая дисциплина"), Type = DisciplineType.Lecture, ColorHex = "#4E79A7" }; break;
                case "Учебная группа": row = new GroupRow { Name = UniqueName("Новая группа"), Course = 1, StudentCount = 25 }; break;
                case "Запись плана": row = new PlanRow { LessonsPerWeek = 1 }; break;
                case "Слот": row = new SlotRow { Order = Rows.Count + 1, StartMinutes = 480, EndMinutes = 570 }; break;
                default: row = new PeriodRow { Name = UniqueName("Новый период"), Type = PeriodType.Week, StartDate = DateTime.Today, DurationWeeks = 16 }; break;
            }
            Rows.Add(row);
            SelectedItem = row;
        }

        /// <summary>Удаляет выбранную запись из базы данных и из таблицы.</summary>
        public bool RemoveSelected()
        {
            var row = SelectedItem;
            if (row == null) return false;

            var id = row is IDisplayRow display ? display.Id : 0;
            if (id > 0)
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                    try
                    {
                        _remove(db, row);
                    }
                    catch (DbUpdateException ex)
                    {
                        throw new InvalidOperationException(
                            "Не удалось удалить запись: она используется в других данных " +
                            "(например, в расписании или учебном плане). Сначала удалите связанные записи.\n\n" +
                            (ex.InnerException?.Message ?? ex.Message), ex);
                    }
                }
            }

            Rows.Remove(row);
            SelectedItem = null;
            return true;
        }

        /// <summary>Проверяет данные раздела перед сохранением. Возвращает текст проблемы или null.</summary>
        public string Validate()
        {
            switch (_entityName)
            {
                case "Аудитория":
                    if (Rows.OfType<AuditoriumRow>().Any(r => string.IsNullOrWhiteSpace(r.Number)))
                        return "Укажите номер аудитории.";
                    var room = Rows.OfType<AuditoriumRow>()
                        .GroupBy(r => r.Number.Trim(), StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault(g => g.Count() > 1);
                    if (room != null) return $"Номер аудитории «{room.Key}» повторяется.";
                    break;

                case "Преподаватель":
                    if (Rows.OfType<TeacherRow>().Any(r => string.IsNullOrWhiteSpace(r.FullName)))
                        return "Укажите ФИО преподавателя.";
                    break;

                case "Дисциплина":
                    if (Rows.OfType<DisciplineRow>().Any(r => string.IsNullOrWhiteSpace(r.Name)))
                        return "Укажите название дисциплины.";
                    break;

                case "Учебная группа":
                    if (Rows.OfType<GroupRow>().Any(r => string.IsNullOrWhiteSpace(r.Name)))
                        return "Укажите название учебной группы.";
                    var group = Rows.OfType<GroupRow>()
                        .GroupBy(r => r.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault(g => g.Count() > 1);
                    if (group != null) return $"Название группы «{group.Key}» повторяется.";
                    break;

                case "Период":
                    if (Rows.OfType<PeriodRow>().Any(r => string.IsNullOrWhiteSpace(r.Name)))
                        return "Укажите название периода расписания.";
                    break;
            }
            return null;
        }

        public void Apply()
        {
            var typed = new List<object>(Rows);
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                _apply(db, typed);
            }
            Reload();
        }

        /// <summary>Перечитывает строки раздела из базы данных.</summary>
        public void Reload()
        {
            SelectedItem = null;
            Rows.Clear();
            foreach (var r in LoadRows()) Rows.Add(r);
        }

        /// <summary>Генерирует имя, которого ещё нет в таблице.</summary>
        private string UniqueName(string prefix)
        {
            var name = prefix;
            int i = 1;
            while (Rows.OfType<IDisplayRow>().Any(r => string.Equals(r.Display, name, StringComparison.OrdinalIgnoreCase)))
            {
                i++;
                name = $"{prefix} {i}";
            }
            return name;
        }

        /// <summary>Подбирает свободный номер аудитории.</summary>
        private string NextRoomNumber()
        {
            var numbers = Rows.OfType<AuditoriumRow>()
                .Select(r => int.TryParse(r.Number, out var value) ? value : 0)
                .ToList();
            var candidate = (numbers.Count > 0 ? numbers.Max() : 100) + 1;
            while (numbers.Contains(candidate)) candidate++;
            return candidate.ToString();
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
