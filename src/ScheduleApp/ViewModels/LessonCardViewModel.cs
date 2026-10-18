using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ScheduleApp.Domain;
using ScheduleApp.Domain.Entities;
using ScheduleApp.Services.Data;

namespace ScheduleApp.ViewModels
{
    /// <summary>Карточка занятия: просмотр, редактирование, добавление с валидацией.</summary>
    public sealed class LessonCardViewModel : ObservableObject
    {
        private readonly ScheduleSnapshot _snap;
        private readonly Lesson _lesson;
        private readonly bool _isNew;

        private NamedItem<Teacher> _teacher;
        private NamedItem<Auditorium> _room;
        private NamedItem<StudyPlanItem> _planItem;
        private WeekDay _day;
        private NamedItem<TimeSlot> _slot;
        private string _validationMessage;
        private bool _isLocked;

        public LessonCardViewModel(ScheduleSnapshot snap, Lesson lesson, bool isNew)
        {
            _snap = snap;
            _lesson = lesson;
            _isNew = isNew;

            Title = isNew ? "Добавление занятия" : "Карточка занятия";

            foreach (var t in snap.Teachers)
                Teachers.Add(new NamedItem<Teacher> { Value = t, Name = $"{t.FullName} — {t.Department}" });
            foreach (var r in snap.Rooms)
                Rooms.Add(new NamedItem<Auditorium> { Value = r, Name = $"{r.Number} ({r.Type}) · {r.Capacity} мест · {r.Building}" });
            foreach (var s in snap.Slots)
                Slots.Add(new NamedItem<TimeSlot> { Value = s, Name = $"Пара {s.Order}: {s.Display}" });
            foreach (var d in Enum.GetValues(typeof(WeekDay)))
                Days.Add((WeekDay)d);

            _day = lesson.Day;
            _slot = Slots.FirstOrDefault(x => x.Value.Id == lesson.TimeSlotId) ?? Slots.FirstOrDefault();
            _teacher = Teachers.FirstOrDefault(x => x.Value.Id == lesson.TeacherId);
            _room = Rooms.FirstOrDefault(x => x.Value.Id == lesson.AuditoriumId);
            _isLocked = lesson.IsLocked;

            SaveCommand = new RelayCommand(Save, () => ValidationMessage == null && CanSave);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public string Title { get; }
        public event Action<bool> RequestClose;
        public Lesson Result { get; private set; }

        public RelayCommand SaveCommand { get; }
        public RelayCommand CancelCommand { get; }

        public ObservableCollection<NamedItem<Teacher>> Teachers { get; } = new ObservableCollection<NamedItem<Teacher>>();
        public ObservableCollection<NamedItem<Auditorium>> Rooms { get; } = new ObservableCollection<NamedItem<Auditorium>>();
        public ObservableCollection<NamedItem<TimeSlot>> Slots { get; } = new ObservableCollection<NamedItem<TimeSlot>>();
        public ObservableCollection<NamedItem<StudyPlanItem>> PlanItems { get; } = new ObservableCollection<NamedItem<StudyPlanItem>>();
        public List<WeekDay> Days { get; } = new List<WeekDay>();

        public bool CanSave => _teacher?.Value != null && _room?.Value != null && _slot?.Value != null &&
                               PlanItems.Any();

        public bool IsNew => _isNew;
        public bool ShowLockOption => !_isNew;

        public bool IsLocked
        {
            get => _isLocked;
            set { if (Set(ref _isLocked, value)) Validate(); }
        }

        public WeekDay Day
        {
            get => _day;
            set { if (Set(ref _day, value)) Validate(); }
        }

        public NamedItem<TimeSlot> Slot
        {
            get => _slot;
            set { if (Set(ref _slot, value)) Validate(); }
        }

        public NamedItem<Teacher> Teacher
        {
            get => _teacher;
            set
            {
                if (Set(ref _teacher, value))
                {
                    FillPlanItems();
                    Validate();
                }
            }
        }

        public NamedItem<Auditorium> Room
        {
            get => _room;
            set { if (Set(ref _room, value)) Validate(); }
        }

        public NamedItem<StudyPlanItem> PlanItem
        {
            get => _planItem;
            set { if (Set(ref _planItem, value)) Validate(); }
        }

        public string ValidationMessage
        {
            get => _validationMessage;
            private set
            {
                if (Set(ref _validationMessage, value))
                {
                    Raise(nameof(HasValidation));
                    Raise(nameof(CanSave));
                }
            }
        }

        public bool HasValidation => !string.IsNullOrEmpty(_validationMessage);

        private void FillPlanItems()
        {
            PlanItems.Clear();
            if (_teacher?.Value == null) return;
            var items = _snap.Plan.Where(p => p.TeacherId == _teacher.Value.Id).ToList();
            if (items.Count == 0) items = _snap.Plan.Take(30).ToList();
            foreach (var p in items)
            {
                var disc = _snap.Disciplines.FirstOrDefault(d => d.Id == p.DisciplineId);
                var group = _snap.Groups.FirstOrDefault(g => g.Id == p.GroupId);
                PlanItems.Add(new NamedItem<StudyPlanItem>
                {
                    Value = p,
                    Name = $"{disc?.Name} ({disc?.Type}) — {group?.Name}, {p.LessonsPerWeek} зан./нед."
                });
            }
            _planItem = PlanItems.FirstOrDefault(x => _isNew ? true : x.Value.Id == _lesson.StudyPlanItemId)
                        ?? PlanItems.FirstOrDefault();
            Raise(nameof(PlanItem));
        }

        private void Validate()
        {
            if (_teacher?.Value == null || _room?.Value == null || _slot?.Value == null)
            {
                ValidationMessage = "Выберите преподавателя, аудиторию и время";
                return;
            }
            var reasons = new List<string>();
            var lessons = _snap.Lessons.Where(l => _isNew || l.Id != _lesson.Id)
                .Where(l => l.Day == Day && l.TimeSlotId == _slot.Value.Id).ToList();

            if (lessons.Any(l => l.TeacherId == _teacher.Value.Id))
                reasons.Add($"преподаватель {_teacher.Value.FullName} уже занят");
            if (lessons.Any(l => l.AuditoriumId == _room.Value.Id))
                reasons.Add($"аудитория {_room.Value.Number} уже занята");
            foreach (var g in SelectedGroups)
                if (lessons.Any(l => l.LessonGroups.Any(x => x.GroupId == g.Id)))
                    reasons.Add($"группа {g.Name} уже имеет занятие");

            foreach (var p in _snap.Preferences.Where(p => p.IsHard && p.TeacherId == _teacher.Value.Id))
            {
                if (p.Kind == PreferenceKind.ForbiddenDay && p.Day == Day)
                    reasons.Add($"{_teacher.Value.FullName}: запрещённый день");
                if (p.Kind == PreferenceKind.ForbiddenTime && p.TimeSlotId == _slot.Value.Id)
                    reasons.Add($"{_teacher.Value.FullName}: запрещённое время");
            }

            var discType = _planItem?.Value != null
                ? _snap.Disciplines.FirstOrDefault(d => d.Id == _planItem.Value.DisciplineId)?.Type
                : (DisciplineType?)null;
            if (discType.HasValue && !Services.Scheduling.ConstraintEngine.RoomMatches(discType.Value, _room.Value.Type))
                reasons.Add($"тип аудитории ({_room.Value.Type}) не рекомендован для дисциплины ({discType.Value}) — сохранение возможно");

            var maxStudents = SelectedGroups.Count == 0 ? 0 : SelectedGroups.Max(g => g.StudentCount);
            if (_room.Value.Capacity < maxStudents)
                reasons.Add($"в аудитории {_room.Value.Number} меньше мест ({_room.Value.Capacity}), чем студентов ({maxStudents})");

            var hard = reasons.Where(r => !r.Contains("не рекомендован")).ToList();
            var soft = reasons.Where(r => r.Contains("не рекомендован")).ToList();

            if (hard.Count > 0)
                ValidationMessage = "Нельзя сохранить: " + string.Join("; ", hard) +
                                    (soft.Count > 0 ? "\nТакже: " + string.Join("; ", soft) : "");
            else
                ValidationMessage = soft.Count > 0 ? "Предупреждение: " + string.Join("; ", soft) : null;
        }

        public List<StudentGroup> SelectedGroups { get; } = new List<StudentGroup>();

        public void SetSelectedGroups(IEnumerable<int> groupIds)
        {
            SelectedGroups.Clear();
            foreach (var id in groupIds)
            {
                var g = _snap.Groups.FirstOrDefault(x => x.Id == id);
                if (g != null) SelectedGroups.Add(g);
            }
            Validate();
        }

        private void Save()
        {
            if (_teacher?.Value == null || _room?.Value == null || _slot?.Value == null) return;
            if (HasValidation && _validationMessage.StartsWith("Нельзя сохранить")) return;

            _lesson.Day = Day;
            _lesson.TimeSlotId = _slot.Value.Id;
            _lesson.TeacherId = _teacher.Value.Id;
            _lesson.AuditoriumId = _room.Value.Id;
            _lesson.IsLocked = _isLocked;
            _lesson.StudyPlanItemId = _planItem?.Value?.Id;
            if (_planItem?.Value != null)
            {
                _lesson.DisciplineId = _planItem.Value.DisciplineId;
                if (SelectedGroups.Count == 0)
                {
                    var g = _snap.Groups.FirstOrDefault(x => x.Id == _planItem.Value.GroupId);
                    if (g != null) SelectedGroups.Add(g);
                }
            }
            _lesson.LessonGroups = SelectedGroups.Select(g => new LessonGroup { GroupId = g.Id }).ToList();

            Result = _lesson;
            RequestClose?.Invoke(true);
        }
    }
}
