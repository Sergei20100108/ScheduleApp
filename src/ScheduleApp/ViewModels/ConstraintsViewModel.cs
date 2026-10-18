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
    public sealed class PreferenceRow : ObservableObject
    {
        public int Id { get; set; }
        public PreferenceKind Kind { get; set; }
        public WeekDay? Day { get; set; }
        public int? TimeSlotId { get; set; }
        public int? AuditoriumId { get; set; }
        public int Weight { get; set; } = 50;
        public bool IsHard { get; set; }

        public string Description
        {
            get
            {
                switch (Kind)
                {
                    case PreferenceKind.ForbiddenDay: return $"Запрещён: {DayName(Day)}";
                    case PreferenceKind.ForbiddenTime: return "Запрещённое время (см. слот)";
                    case PreferenceKind.PreferredDay: return $"Предпочтителен: {DayName(Day)}";
                    case PreferenceKind.PreferredTime: return "Предпочтительное время (см. слот)";
                    case PreferenceKind.PreferredAuditorium: return "Предпочтительная аудитория";
                    case PreferenceKind.AvoidAuditorium: return "Нежелательная аудитория";
                    default: return Kind.ToString();
                }
            }
        }

        private static string DayName(WeekDay? d) => d.HasValue ? Services.Validation.ValidationService.DayFullName(d.Value) : "—";
    }

    /// <summary>Вкладка «Ограничения»: предпочтения преподавателей, жёсткие/мягкие, веса.</summary>
    public sealed class ConstraintsViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IDialogService _dialogs;
        private readonly Action _onDataChanged;

        private NamedItem<Teacher> _selectedTeacher;
        private string _statusText = "";

        public ConstraintsViewModel(IServiceScopeFactory scopeFactory, IDialogService dialogs, Action onDataChanged)
        {
            _scopeFactory = scopeFactory;
            _dialogs = dialogs;
            _onDataChanged = onDataChanged;

            AddCommand = new RelayCommand(AddPreference, () => SelectedTeacher?.Value != null);
            RemoveCommand = new RelayCommand(RemovePreference, () => SelectedRow != null);
            SaveCommand = new RelayCommand(Save, () => SelectedTeacher?.Value != null);
            LoadCommand = new RelayCommand(LoadForSelectedTeacher);

            LoadTeachers();
        }

        public RelayCommand AddCommand { get; }
        public RelayCommand RemoveCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand LoadCommand { get; }

        public ObservableCollection<NamedItem<Teacher>> Teachers { get; } = new ObservableCollection<NamedItem<Teacher>>();
        public ObservableCollection<PreferenceRow> Rows { get; } = new ObservableCollection<PreferenceRow>();
        public ObservableCollection<TimeSlot> Slots { get; } = new ObservableCollection<TimeSlot>();
        public ObservableCollection<Auditorium> Auditoriums { get; } = new ObservableCollection<Auditorium>();

        public Array PreferenceKinds => Enum.GetValues(typeof(PreferenceKind));
        public Array WeekDays => Enum.GetValues(typeof(WeekDay));

        public string StatusText
        {
            get => _statusText;
            set => Set(ref _statusText, value);
        }

        public NamedItem<Teacher> SelectedTeacher
        {
            get => _selectedTeacher;
            set { if (Set(ref _selectedTeacher, value)) LoadForSelectedTeacher(); }
        }

        public PreferenceRow SelectedRow { get; set; }

        public string HardHint =>
            "Жёсткие ограничения (IsHard) непреодолимы: генератор никогда не поставит занятие в запрещённый день/время.\n" +
            "Мягкие учитываются через вес 0–100: чем выше сумма соблюдённых весов, тем выше оценка качества расписания.";

        private void LoadTeachers()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                foreach (var t in db.Teachers.AsNoTracking().OrderBy(t => t.FullName).ToList())
                    Teachers.Add(new NamedItem<Teacher> { Value = t, Name = $"{t.FullName} — {t.Department}" });
                foreach (var s in db.TimeSlots.AsNoTracking().OrderBy(s => s.Order).ToList())
                    Slots.Add(s);
                foreach (var a in db.Auditoriums.AsNoTracking().OrderBy(a => a.Number).ToList())
                    Auditoriums.Add(a);
            }
            SelectedTeacher = Teachers.Skip(1).FirstOrDefault() ?? Teachers.FirstOrDefault();
        }

        private void LoadForSelectedTeacher()
        {
            Rows.Clear();
            if (SelectedTeacher?.Value == null) return;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                var prefs = db.TeacherPreferences.AsNoTracking()
                    .Where(p => p.TeacherId == SelectedTeacher.Value.Id)
                    .OrderByDescending(p => p.IsHard).ThenBy(p => p.Kind).ToList();
                foreach (var p in prefs)
                {
                    Rows.Add(new PreferenceRow
                    {
                        Id = p.Id,
                        Kind = p.Kind,
                        Day = p.Day,
                        TimeSlotId = p.TimeSlotId,
                        AuditoriumId = p.AuditoriumId,
                        Weight = p.Weight,
                        IsHard = p.IsHard
                    });
                }
            }
            StatusText = $"Предпочтений: {Rows.Count}";
        }

        private void AddPreference()
        {
            Rows.Add(new PreferenceRow { Kind = PreferenceKind.PreferredDay, Day = WeekDay.Monday, Weight = 50 });
        }

        private void RemovePreference()
        {
            if (SelectedRow == null) return;
            Rows.Remove(SelectedRow);
            SelectedRow = null;
        }

        private void Save()
        {
            if (SelectedTeacher?.Value == null) return;
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                    var existing = db.TeacherPreferences.Where(p => p.TeacherId == SelectedTeacher.Value.Id).ToList();
                    db.TeacherPreferences.RemoveRange(existing);
                    foreach (var row in Rows)
                    {
                        db.TeacherPreferences.Add(new TeacherPreference
                        {
                            TeacherId = SelectedTeacher.Value.Id,
                            Kind = row.Kind,
                            Day = row.Day,
                            TimeSlotId = row.TimeSlotId,
                            AuditoriumId = row.AuditoriumId,
                            Weight = Math.Max(0, Math.Min(100, row.Weight)),
                            IsHard = row.IsHard
                        });
                    }
                    db.SaveChanges();
                }
                _onDataChanged?.Invoke();
                LoadForSelectedTeacher();
                StatusText = "Ограничения сохранены";
            }
            catch (Exception ex)
            {
                _dialogs.Error($"Не удалось сохранить ограничения: {ex.Message}");
            }
        }
    }
}
