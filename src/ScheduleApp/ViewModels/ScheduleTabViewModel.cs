using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ScheduleApp.Domain;
using ScheduleApp.Domain.Entities;
using ScheduleApp.Services.Data;
using ScheduleApp.Services.Dialogs;
using ScheduleApp.Services.Export;
using ScheduleApp.Services.Settings;
using ScheduleApp.Services.Validation;
using ScheduleApp.ViewModels.ScheduleGrid;

namespace ScheduleApp.ViewModels
{
    public sealed class NamedItem<T>
    {
        public T Value { get; set; }
        public string Name { get; set; }
        public override string ToString() => Name;
    }

    /// <summary>Цель переноса/добавления в шахматке (определяется View-слоем).</summary>
    public sealed class CellTarget
    {
        public GridCell Cell { get; set; }
    }

    public sealed class ScheduleTabViewModel : ObservableObject
    {
        private readonly IScheduleDataService _data;
        private readonly IValidationService _validation;
        private readonly IDialogService _dialogs;
        private readonly IExcelExportService _excel;
        private readonly IPdfExportService _pdf;
        private readonly ISettingsService _settings;
        private readonly Action _onDataChanged;

        private ScheduleSnapshot _snap = new ScheduleSnapshot();
        private SchedulePeriod _period;
        private GridViewMode _viewMode = GridViewMode.Week;
        private GroupingMode _grouping = GroupingMode.None;
        private SummaryDimension _summaryDimension = SummaryDimension.Groups;
        private bool _transposed;
        private double _zoom = 100;
        private NamedItem<Auditorium> _selectedRoom;
        private NamedItem<Teacher> _selectedTeacher;
        private NamedItem<StudentGroup> _selectedGroup;
        private NamedItem<Discipline> _selectedDiscipline;
        private NamedItem<Auditorium> _contextRoom;
        private NamedItem<Teacher> _contextTeacher;
        private string _statusText = "Готово";
        private ScheduleGridViewModel _grid = new ScheduleGridViewModel();
        private bool _isBusy;

        public ScheduleTabViewModel(IScheduleDataService data, IValidationService validation,
            IDialogService dialogs, IExcelExportService excel, IPdfExportService pdf,
            ISettingsService settings, Action onDataChanged)
        {
            _data = data;
            _validation = validation;
            _dialogs = dialogs;
            _excel = excel;
            _pdf = pdf;
            _settings = settings;
            _onDataChanged = onDataChanged;

            RefreshCommand = new RelayCommand(async () => await RefreshAsync(), () => !IsBusy);
            OpenLessonCommand = new RelayCommand<LessonTile>(OpenLessonCard, t => t != null);
            ToggleLockCommand = new RelayCommand<LessonTile>(ToggleLock, t => t != null);
            DeleteLessonCommand = new RelayCommand<LessonTile>(DeleteLesson, t => t != null);
            AddLessonCommand = new RelayCommand<CellTarget>(AddLessonAt, t => t != null);
            ExportExcelCommand = new RelayCommand(async () => await ExportAsync(isPdf: false), () => !IsBusy);
            ExportPdfCommand = new RelayCommand(async () => await ExportAsync(isPdf: true), () => !IsBusy);
            ClearFiltersCommand = new RelayCommand(ClearFilters);
        }

        public RelayCommand RefreshCommand { get; }
        public RelayCommand<LessonTile> OpenLessonCommand { get; }
        public RelayCommand<LessonTile> ToggleLockCommand { get; }
        public RelayCommand<LessonTile> DeleteLessonCommand { get; }
        public RelayCommand<CellTarget> AddLessonCommand { get; }
        public RelayCommand ExportExcelCommand { get; }
        public RelayCommand ExportPdfCommand { get; }
        public RelayCommand ClearFiltersCommand { get; }

        public ObservableCollection<NamedItem<SchedulePeriod>> Periods { get; } = new ObservableCollection<NamedItem<SchedulePeriod>>();
        public ObservableCollection<NamedItem<Auditorium>> Rooms { get; } = new ObservableCollection<NamedItem<Auditorium>>();
        public ObservableCollection<NamedItem<Teacher>> Teachers { get; } = new ObservableCollection<NamedItem<Teacher>>();
        public ObservableCollection<NamedItem<StudentGroup>> Groups { get; } = new ObservableCollection<NamedItem<StudentGroup>>();
        public ObservableCollection<NamedItem<Discipline>> Disciplines { get; } = new ObservableCollection<NamedItem<Discipline>>();

        public Array ViewModes => Enum.GetValues(typeof(GridViewMode));
        public Array GroupingModes => Enum.GetValues(typeof(GroupingMode));
        public Array SummaryDimensions => Enum.GetValues(typeof(SummaryDimension));

        public ScheduleGridViewModel Grid
        {
            get => _grid;
            set => Set(ref _grid, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => Set(ref _isBusy, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => Set(ref _statusText, value);
        }

        public IDialogService Dialogs => _dialogs;

        public SchedulePeriod Period
        {
            get => _period;
            set { if (Set(ref _period, value)) RebuildGrid(); }
        }

        public GridViewMode ViewMode
        {
            get => _viewMode;
            set
            {
                if (Set(ref _viewMode, value))
                {
                    Raise(nameof(IsByEntityMode));
                    Raise(nameof(ShowEntityPickers));
                    Raise(nameof(IsSummaryMode));
                    RebuildGrid();
                }
            }
        }

        public bool IsByEntityMode =>
            _viewMode == GridViewMode.ByAuditorium || _viewMode == GridViewMode.ByTeacher;

        public bool ShowEntityPickers => IsByEntityMode;

        public bool IsSummaryMode => _viewMode == GridViewMode.Summary;

        public GroupingMode Grouping
        {
            get => _grouping;
            set { if (Set(ref _grouping, value)) RebuildGrid(); }
        }

        public SummaryDimension SummaryDimension
        {
            get => _summaryDimension;
            set { if (Set(ref _summaryDimension, value)) RebuildGrid(); }
        }

        public bool Transposed
        {
            get => _transposed;
            set { if (Set(ref _transposed, value)) RebuildGrid(); }
        }

        public double Zoom
        {
            get => _zoom;
            set { if (Set(ref _zoom, value)) { Raise(nameof(CellWidth)); Raise(nameof(CellHeight)); Raise(nameof(TileFontSize)); Raise(nameof(HeaderFontSize)); } }
        }

        public double CellWidth => Math.Round(150 * _zoom / 100.0);
        public double CellHeight => Math.Round(64 * _zoom / 100.0);
        public double TileFontSize => Math.Max(6.5, Math.Round(9.5 * _zoom / 100.0, 1));
        public double HeaderFontSize => Math.Max(7.5, Math.Round(11 * _zoom / 100.0, 1));

        public NamedItem<Auditorium> SelectedRoom
        {
            get => _selectedRoom;
            set { if (Set(ref _selectedRoom, value)) RebuildGrid(); }
        }

        public NamedItem<Teacher> SelectedTeacher
        {
            get => _selectedTeacher;
            set { if (Set(ref _selectedTeacher, value)) RebuildGrid(); }
        }

        public NamedItem<StudentGroup> SelectedGroup
        {
            get => _selectedGroup;
            set { if (Set(ref _selectedGroup, value)) RebuildGrid(); }
        }

        public NamedItem<Discipline> SelectedDiscipline
        {
            get => _selectedDiscipline;
            set { if (Set(ref _selectedDiscipline, value)) RebuildGrid(); }
        }

        public NamedItem<Auditorium> ContextRoom
        {
            get => _contextRoom;
            set { if (Set(ref _contextRoom, value)) RebuildGrid(); }
        }

        public NamedItem<Teacher> ContextTeacher
        {
            get => _contextTeacher;
            set { if (Set(ref _contextTeacher, value)) RebuildGrid(); }
        }

        public async Task InitializeAsync()
        {
            await RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            try
            {
                IsBusy = true;
                StatusText = "Загрузка данных…";

                // Период для загрузки: ранее выбранный либо первый из уже известного списка.
                // На первом запуске список периодов ещё пуст, поэтому id = 0 (метаданные без занятий).
                var requestedId = Period?.Id ?? _snap.Periods.FirstOrDefault()?.Id ?? 0;
                var snap = await Task.Run(() => _data.Load(requestedId));

                // Первый запуск: загружаем данные повторно уже для конкретного периода,
                // иначе занятия не попадут в шахматку (Load(0) отдаёт только справочники).
                if (requestedId == 0)
                {
                    var firstId = snap.Periods.FirstOrDefault()?.Id ?? 0;
                    if (firstId != 0)
                        snap = await Task.Run(() => _data.Load(firstId));
                }

                _snap = snap;

                var keepPeriodId = Period?.Id ?? snap.Periods.FirstOrDefault()?.Id ?? 0;
                Periods.Clear();
                foreach (var p in snap.Periods)
                    Periods.Add(new NamedItem<SchedulePeriod> { Value = p, Name = $"{p.Name} ({p.StartDate:dd.MM.yyyy}, {p.DurationWeeks} нед.)" });
                Period = snap.Periods.FirstOrDefault(p => p.Id == keepPeriodId) ?? snap.Periods.FirstOrDefault();

                FillLookups();
                RebuildGrid();
                StatusText = $"Занятий в периоде: {_snap.Lessons.Count}";
            }
            catch (Exception ex)
            {
                _dialogs.Error($"Не удалось загрузить расписание: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Обновление только занятий (после правок) без перезагрузки справочников.</summary>
        public void ReloadLessons()
        {
            if (Period == null) return;
            var snap = _data.Load(Period.Id);
            _snap.Lessons = snap.Lessons;
            _snap.Preferences = snap.Preferences;
            RebuildGrid();
        }

        private void FillLookups()
        {
            Rooms.Clear();
            Rooms.Add(new NamedItem<Auditorium> { Value = null, Name = "Все аудитории" });
            foreach (var r in _snap.Rooms)
                Rooms.Add(new NamedItem<Auditorium> { Value = r, Name = $"{r.Number} ({r.Type}) · {r.Capacity} мест" });

            Teachers.Clear();
            Teachers.Add(new NamedItem<Teacher> { Value = null, Name = "Все преподаватели" });
            foreach (var t in _snap.Teachers)
                Teachers.Add(new NamedItem<Teacher> { Value = t, Name = $"{t.FullName} — {t.Department}" });

            Groups.Clear();
            Groups.Add(new NamedItem<StudentGroup> { Value = null, Name = "Все группы" });
            foreach (var g in _snap.Groups)
                Groups.Add(new NamedItem<StudentGroup> { Value = g, Name = $"{g.Name} ({g.Course} курс, {g.StudentCount} чел.)" });

            Disciplines.Clear();
            Disciplines.Add(new NamedItem<Discipline> { Value = null, Name = "Все дисциплины" });
            foreach (var d in _snap.Disciplines)
                Disciplines.Add(new NamedItem<Discipline> { Value = d, Name = $"{d.Name} ({d.Type})" });

            if (ContextRoom == null)
            {
                ContextRoom = Rooms.Skip(1).FirstOrDefault();
                ContextTeacher = Teachers.Skip(1).FirstOrDefault();
            }
        }

        private void ClearFilters()
        {
            SelectedRoom = null;
            SelectedTeacher = null;
            SelectedGroup = null;
            SelectedDiscipline = null;
        }

        public void RebuildGrid()
        {
            if (Period == null || _snap.Slots.Count == 0) return;

            HashSet<int> conflictIds;
            try
            {
                conflictIds = _validation.ValidatePeriod(Period.Id).ConflictLessonIds;
            }
            catch
            {
                conflictIds = new HashSet<int>(); // подсветка конфликтов не должна блокировать отображение
            }

            var allowedDays = new List<WeekDay>
            {
                WeekDay.Monday, WeekDay.Tuesday, WeekDay.Wednesday, WeekDay.Thursday, WeekDay.Friday, WeekDay.Saturday
            };

            var s = _settings.Current;
            var req = new GridRequest
            {
                Mode = ViewMode,
                Grouping = Grouping,
                SummaryDimension = SummaryDimension,
                Transposed = Transposed,
                AuditoriumId = ContextRoom?.Value?.Id,
                TeacherId = ContextTeacher?.Value?.Id,
                FilterAuditoriumId = SelectedRoom?.Value?.Id,
                FilterTeacherId = SelectedTeacher?.Value?.Id,
                FilterGroupId = SelectedGroup?.Value?.Id,
                FilterDisciplineId = SelectedDiscipline?.Value?.Id,
                HighlightEmpty = s.HighlightEmptyCells,
                ShowTeacher = s.ShowTeacherInTile,
                ShowGroups = s.ShowGroupsInTile,
                ShowRoom = s.ShowRoomInTile,
                Period = Period,
                Slots = _snap.Slots,
                Lessons = _snap.Lessons,
                AllowedDays = allowedDays,
                ConflictLessonIds = conflictIds,
                RoomNames = _snap.Rooms.ToDictionary(r => r.Id, r => r.Number),
                TeacherNames = _snap.Teachers.ToDictionary(t => t.Id, t => t.FullName),
                GroupNames = _snap.Groups.ToDictionary(g => g.Id, g => g.Name),
                DisciplineNames = _snap.Disciplines.ToDictionary(d => d.Id, d => d.Name)
            };
            Grid = ScheduleGridBuilder.Build(req);
        }

        // ---- Редактирование из шахматки ----

        public string ValidateMove(LessonTile tile, GridCell cell)
        {
            if (tile == null || cell == null || Period == null) return "Некорректный перенос";
            var lesson = _snap.Lessons.FirstOrDefault(l => l.Id == tile.LessonId);
            if (lesson == null) return "Занятие не найдено";
            if (lesson.IsLocked) return "Занятие заблокировано — снимите блокировку, чтобы перенести его";
            if (cell.Day < 0) return "Эта ячейка не поддерживает перенос";

            var reasons = new List<string>();
            var sameCell = _snap.Lessons.Where(l => l.Id != lesson.Id &&
                l.Day == (WeekDay)cell.Day && l.TimeSlotId == cell.SlotId).ToList();

            if (sameCell.Any(l => l.TeacherId == lesson.TeacherId))
                reasons.Add($"преподаватель {lesson.Teacher.FullName} уже ведёт занятие в этот слот");

            var targetRoomId = ViewMode == GridViewMode.ByAuditorium && cell.RoomId > 0 ? cell.RoomId : lesson.AuditoriumId;
            if (sameCell.Any(l => l.AuditoriumId == targetRoomId))
            {
                var room = _snap.Rooms.FirstOrDefault(r => r.Id == targetRoomId);
                reasons.Add($"аудитория {room?.Number ?? targetRoomId.ToString()} уже занята");
            }
            foreach (var lg in lesson.LessonGroups)
                if (sameCell.Any(l => l.LessonGroups.Any(x => x.GroupId == lg.GroupId)))
                    reasons.Add($"группа {lg.Group?.Name ?? lg.GroupId.ToString()} уже имеет занятие в этот слот");

            foreach (var p in _snap.Preferences.Where(p => p.IsHard && p.TeacherId == lesson.TeacherId))
            {
                if (p.Kind == PreferenceKind.ForbiddenDay && p.Day == (WeekDay)cell.Day)
                    reasons.Add("преподавателю запрещён этот день");
                if (p.Kind == PreferenceKind.ForbiddenTime && p.TimeSlotId == cell.SlotId)
                    reasons.Add("преподавателю запрещено это время");
            }
            return reasons.Count == 0 ? null : "Нельзя перенести: " + string.Join("; ", reasons);
        }

        public void ExecuteMove(LessonTile tile, GridCell cell)
        {
            var error = ValidateMove(tile, cell);
            if (error != null)
            {
                StatusText = error;
                _dialogs.Warn(error + "\n\nПеренос отменён.");
                return;
            }
            var lesson = _snap.Lessons.First(l => l.Id == tile.LessonId);
            var stored = _data.GetLesson(lesson.Id);
            stored.Day = (WeekDay)cell.Day;
            stored.TimeSlotId = cell.SlotId;
            if (ViewMode == GridViewMode.ByAuditorium && cell.RoomId > 0)
                stored.AuditoriumId = cell.RoomId;
            _data.SaveLesson(stored);

            StatusText = $"Занятие «{lesson.Discipline?.Name}» перенесено";
            ReloadLessons();
            _onDataChanged?.Invoke();
        }

        private void OpenLessonCard(LessonTile tile)
        {
            var lesson = _data.GetLesson(tile.LessonId);
            if (lesson == null) return;
            var vm = new LessonCardViewModel(_snap, lesson, isNew: false);
            var window = new Views.LessonCardWindow { DataContext = vm };
            if (window.ShowDialog() == true && vm.Result != null)
            {
                _data.SaveLesson(vm.Result);
                StatusText = "Занятие обновлено";
                ReloadLessons();
                _onDataChanged?.Invoke();
            }
        }

        private void AddLessonAt(CellTarget target)
        {
            if (target?.Cell == null || Period == null) return;
            var lesson = new Lesson
            {
                PeriodId = Period.Id,
                Day = (WeekDay)Math.Max(0, target.Cell.Day),
                TimeSlotId = target.Cell.SlotId,
                AuditoriumId = ViewMode == GridViewMode.ByAuditorium && ContextRoom?.Value != null
                    ? ContextRoom.Value.Id
                    : _snap.Rooms.FirstOrDefault()?.Id ?? 0
            };
            var vm = new LessonCardViewModel(_snap, lesson, isNew: true);
            var window = new Views.LessonCardWindow { DataContext = vm };
            if (window.ShowDialog() == true && vm.Result != null)
            {
                _data.SaveLesson(vm.Result);
                StatusText = "Занятие добавлено";
                ReloadLessons();
                _onDataChanged?.Invoke();
            }
        }

        private void ToggleLock(LessonTile tile)
        {
            var stored = _data.GetLesson(tile.LessonId);
            if (stored == null) return;
            stored.IsLocked = !stored.IsLocked;
            _data.SaveLesson(stored);
            StatusText = stored.IsLocked
                ? "Занятие заблокировано (защищено от перегенерации)"
                : "Блокировка снята";
            ReloadLessons();
            _onDataChanged?.Invoke();
        }

        private void DeleteLesson(LessonTile tile)
        {
            var lesson = _snap.Lessons.FirstOrDefault(l => l.Id == tile.LessonId);
            if (lesson == null) return;
            var slot = _snap.Slots.FirstOrDefault(s => s.Id == lesson.TimeSlotId);
            var text = $"Удалить занятие «{lesson.Discipline?.Name}» ({ValidationService.DayFullName(lesson.Day)}, {slot?.Display})?";
            if (lesson.IsLocked)
                text += "\nЗанятие заблокировано; удаление необратимо.";
            if (!_dialogs.Confirm(text, "Удаление занятия")) return;
            _data.DeleteLesson(lesson.Id);
            StatusText = "Занятие удалено";
            ReloadLessons();
            _onDataChanged?.Invoke();
        }

        private async Task ExportAsync(bool isPdf)
        {
            try
            {
                IsBusy = true;
                var model = BuildExportModel();
                var path = isPdf
                    ? _dialogs.SaveFilePath("PDF документ (*.pdf)|*.pdf", "расписание.pdf")
                    : _dialogs.SaveFilePath("Книга Excel (*.xlsx)|*.xlsx", "расписание.xlsx");
                if (path == null) return;
                if (isPdf) await Task.Run(() => _pdf.Export(model, path));
                else await Task.Run(() => _excel.Export(model, path));
                StatusText = $"Экспортировано: {path}";
            }
            catch (Exception ex)
            {
                _dialogs.Error($"Ошибка экспорта: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private ExportGridModel BuildExportModel()
        {
            var model = new ExportGridModel
            {
                Title = $"Расписание занятий — {Grid.Title}",
                Subtitle = Grid.Subtitle,
                GeneratedAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm")
            };
            foreach (var section in Grid.Sections)
            {
                if (!string.IsNullOrEmpty(section.Title))
                {
                    var titleRow = new ExportRow { Header = section.Title };
                    for (var i = 1; i < Math.Max(1, section.Columns.Count); i++) titleRow.Cells.Add(new ExportCell());
                    model.Rows.Add(titleRow);
                }
                if (section.Columns.Count > 0)
                {
                    model.ColumnHeaders = section.Columns.Select(c =>
                        string.IsNullOrEmpty(c.SubHeader) ? c.Header ?? "" : c.Header + "\n" + c.SubHeader).ToList();
                }
                foreach (var row in section.Rows)
                {
                    var er = new ExportRow
                    {
                        Header = string.IsNullOrEmpty(row.SubHeader) ? row.Header ?? "" : row.Header + "\n" + row.SubHeader
                    };
                    foreach (var cell in row.Cells)
                    {
                        var ec = new ExportCell();
                        foreach (var t in cell.Tiles)
                            ec.Tiles.Add(new ExportTile
                            {
                                Text = $"{t.Line1}\n{t.Line2}\n{t.Line3}".Trim('\n'),
                                ColorHex = t.ColorHex
                            });
                        er.Cells.Add(ec);
                    }
                    model.Rows.Add(er);
                }
            }
            return model;
        }
    }
}
