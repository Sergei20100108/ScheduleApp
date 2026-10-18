using System;
using System.Collections.Generic;
using System.Linq;
using ScheduleApp.Domain;
using ScheduleApp.Domain.Entities;
using ScheduleApp.Services.Validation;

namespace ScheduleApp.ViewModels.ScheduleGrid
{
    public sealed class GridRequest
    {
        public GridViewMode Mode = GridViewMode.Week;
        public GroupingMode Grouping = GroupingMode.None;
        public SummaryDimension SummaryDimension = SummaryDimension.Groups;
        public bool Transposed;
        public int? AuditoriumId;
        public int? TeacherId;
        public int? FilterAuditoriumId;
        public int? FilterTeacherId;
        public int? FilterGroupId;
        public int? FilterDisciplineId;
        public bool HighlightEmpty = true;
        public bool ShowTeacher = true;
        public bool ShowGroups = true;
        public bool ShowRoom = true;
        public SchedulePeriod Period;
        public List<TimeSlot> Slots;
        public List<Lesson> Lessons;
        public List<WeekDay> AllowedDays;
        public HashSet<int> ConflictLessonIds;
        public Dictionary<int, string> RoomNames = new Dictionary<int, string>();
        public Dictionary<int, string> TeacherNames = new Dictionary<int, string>();
        public Dictionary<int, string> GroupNames = new Dictionary<int, string>();
        public Dictionary<int, string> DisciplineNames = new Dictionary<int, string>();
    }

    /// <summary>Строит сетку «шахматки» для любого режима отображения.</summary>
    public static class ScheduleGridBuilder
    {
        public static ScheduleGridViewModel Build(GridRequest req)
        {
            var lookup = new DataLookup(req);
            var model = new ScheduleGridViewModel();

            var filtered = Filter(req);
            var subtitleParts = new List<string> { req.Period?.Name ?? "" };
            if (req.FilterAuditoriumId.HasValue) subtitleParts.Add($"аудитория: {lookup.Room(req.FilterAuditoriumId.Value)}");
            if (req.FilterTeacherId.HasValue) subtitleParts.Add($"преподаватель: {lookup.Teacher(req.FilterTeacherId.Value)}");
            if (req.FilterGroupId.HasValue) subtitleParts.Add($"группа: {lookup.Group(req.FilterGroupId.Value)}");
            if (req.FilterDisciplineId.HasValue) subtitleParts.Add($"дисциплина: {lookup.Discipline(req.FilterDisciplineId.Value)}");
            model.Subtitle = string.Join(" · ", subtitleParts.Where(s => !string.IsNullOrEmpty(s)));

            switch (req.Mode)
            {
                case GridViewMode.Week:
                    model.Title = "Недельное расписание";
                    BuildWeek(model, req, filtered, lookup);
                    break;
                case GridViewMode.Month:
                    model.Title = "Месячное расписание";
                    BuildMonth(model, req, filtered, lookup);
                    break;
                case GridViewMode.ByAuditorium:
                    model.Title = $"Аудитория {lookup.Room(req.AuditoriumId ?? 0)}";
                    BuildByEntity(model, req, filtered, lookup, isRoom: true);
                    break;
                case GridViewMode.ByTeacher:
                    model.Title = $"Преподаватель {lookup.Teacher(req.TeacherId ?? 0)}";
                    BuildByEntity(model, req, filtered, lookup, isRoom: false);
                    break;
                case GridViewMode.Summary:
                    model.Title = "Сводная таблица";
                    BuildSummary(model, req, filtered, lookup);
                    break;
            }
            return model;
        }

        private static List<Lesson> Filter(GridRequest req)
        {
            IEnumerable<Lesson> q = req.Lessons;
            if (req.FilterAuditoriumId.HasValue) q = q.Where(l => l.AuditoriumId == req.FilterAuditoriumId.Value);
            if (req.FilterTeacherId.HasValue) q = q.Where(l => l.TeacherId == req.FilterTeacherId.Value);
            if (req.FilterGroupId.HasValue) q = q.Where(l => l.LessonGroups.Any(g => g.GroupId == req.FilterGroupId.Value));
            if (req.FilterDisciplineId.HasValue) q = q.Where(l => l.DisciplineId == req.FilterDisciplineId.Value);
            return q.ToList();
        }

        private static List<GridRow> MakeSlotRows(GridRequest req, List<Lesson> lessons, DataLookup lookup,
            Func<Lesson, int> dayOf, Func<Lesson, int> roomOf, bool forceRoomContext)
        {
            var rows = new List<GridRow>();
            foreach (var slot in req.Slots)
            {
                var row = new GridRow
                {
                    Header = slot.Display,
                    SubHeader = $"Пара {slot.Order}",
                    Key = slot.Id,
                    Cells = new List<GridCell>()
                };
                foreach (var day in req.AllowedDays)
                {
                    var cellLessons = lessons.Where(l => l.TimeSlotId == slot.Id && dayOf(l) == (int)day).ToList();
                    var cell = new GridCell
                    {
                        Key = (day, slot.Id),
                        Day = (int)day,
                        SlotId = slot.Id,
                        RoomId = forceRoomContext && roomOf != null ? -1 : -1,
                        Tiles = cellLessons.Select(l => MakeTile(l, lookup, req)).ToList()
                    };
                    cell.IsEmpty = cell.Tiles.Count == 0;
                    cell.IsEmptyHighlight = cell.IsEmpty && req.HighlightEmpty;
                    cell.Hint = cell.IsEmpty
                        ? "Пустая ячейка. Перетащите занятие сюда или щёлкните правой кнопкой, чтобы добавить."
                        : string.Join("\n", cell.Tiles.Select(t => t.ToolTip));
                    row.Cells.Add(cell);
                }
                rows.Add(row);
            }
            return rows;
        }

        private static List<GridColumn> DayColumns(GridRequest req)
        {
            return req.AllowedDays.Select(d => new GridColumn
            {
                Header = ValidationService.DayName(d),
                SubHeader = ValidationService.DayFullName(d),
                Key = (int)d
            }).ToList();
        }

        private static void BuildWeek(ScheduleGridViewModel model, GridRequest req, List<Lesson> lessons, DataLookup lookup)
        {
            if (req.Grouping == GroupingMode.None)
            {
                AddSection(model, "", req, lessons, lookup, req.AllowedDays, DayColumns(req), req.Transposed);
                return;
            }

            IEnumerable<IGrouping<int, Lesson>> groups;
            Func<int, string> title;
            switch (req.Grouping)
            {
                case GroupingMode.ByTeacher:
                    groups = lessons.GroupBy(l => l.TeacherId);
                    title = id => lookup.Teacher(id);
                    break;
                case GroupingMode.ByAuditorium:
                    groups = lessons.GroupBy(l => l.AuditoriumId);
                    title = id => $"Аудитория {lookup.Room(id)}";
                    break;
                default:
                    groups = lessons.SelectMany(l => l.LessonGroups.Select(g => new { g.GroupId, Lesson = l }))
                        .GroupBy(x => x.GroupId, x => x.Lesson);
                    title = id => $"Группа {lookup.Group(id)}";
                    break;
            }

            var ordered = groups.OrderBy(g => title(g.Key)).ToList();
            foreach (var g in ordered)
            {
                AddSection(model, title(g.Key), req, g.ToList(), lookup, req.AllowedDays, DayColumns(req), req.Transposed);
            }
        }

        private static void AddSection(ScheduleGridViewModel model, string title, GridRequest req,
            List<Lesson> lessons, DataLookup lookup, List<WeekDay> days, List<GridColumn> columns, bool transposed)
        {
            var section = new GridSection { Title = title };
            var rows = MakeSlotRows(req, lessons, lookup, l => (int)l.Day, l => l.AuditoriumId, false);
            if (!transposed)
            {
                section.Columns = columns;
                // первая колонка = время
                section.Columns.Insert(0, new GridColumn { Header = "Время", SubHeader = "Пара", Key = "time" });
                section.Rows = rows;
            }
            else
            {
                // Транспонирование: строки = дни, колонки = слоты
                section.Columns = new List<GridColumn> { new GridColumn { Header = "День", Key = "day" } };
                section.Columns.AddRange(req.Slots.Select(s => new GridColumn { Header = s.Display, Key = s.Id }));
                foreach (var day in days)
                {
                    var row = new GridRow
                    {
                        Header = ValidationService.DayName(day),
                        SubHeader = ValidationService.DayFullName(day),
                        Key = (int)day
                    };
                    foreach (var slot in req.Slots)
                    {
                        var cellLessons = lessons.Where(l => l.TimeSlotId == slot.Id && l.Day == day).ToList();
                        var cell = new GridCell
                        {
                            Key = ((int)day, slot.Id),
                            Day = (int)day,
                            SlotId = slot.Id,
                            Tiles = cellLessons.Select(l => MakeTile(l, lookup, req)).ToList()
                        };
                        cell.IsEmpty = cell.Tiles.Count == 0;
                        cell.IsEmptyHighlight = cell.IsEmpty && req.HighlightEmpty;
                        cell.Hint = cell.IsEmpty ? "Пустая ячейка" : string.Join("\n", cell.Tiles.Select(t => t.ToolTip));
                        row.Cells.Add(cell);
                    }
                    section.Rows.Add(row);
                }
            }
            model.Sections.Add(section);
        }

        private static void BuildMonth(ScheduleGridViewModel model, GridRequest req, List<Lesson> lessons, DataLookup lookup)
        {
            if (req.Period == null) return;
            var start = req.Period.StartDate.Date;
            var end = start.AddDays(req.Period.DurationWeeks * 7);
            var first = new DateTime(start.Year, start.Month, 1);
            var gridStart = first.AddDays(-(((int)first.DayOfWeek + 6) % 7)); // понедельник недели

            var section = new GridSection();
            var weeks = new List<DateTime>();
            for (var w = gridStart; w < end; w = w.AddDays(7)) weeks.Add(w);

            var dates = new List<DateTime>();
            for (var d = gridStart; d < end; d = d.AddDays(1))
                if (d >= start || true) dates.Add(d);

            if (!req.Transposed)
            {
                section.Columns.Add(new GridColumn { Header = "Время", Key = "time" });
                foreach (var date in dates)
                {
                    var inPeriod = date >= start && date < end;
                    section.Columns.Add(new GridColumn
                    {
                        Header = date.ToString("dd.MM") + "\n" + ValidationService.DayName(ToWeekDay(date.DayOfWeek)),
                        Key = date,
                        SubHeader = inPeriod ? "" : (date < start ? "вне периода" : "вне периода")
                    });
                }
                foreach (var slot in req.Slots)
                {
                    var row = new GridRow { Header = slot.Display, Key = slot.Id };
                    foreach (var date in dates)
                    {
                        var wd = ToWeekDay(date.DayOfWeek);
                        var inPeriod = date >= start && date < end;
                        var cellLessons = inPeriod
                            ? lessons.Where(l => l.TimeSlotId == slot.Id && l.Day == wd).ToList()
                            : new List<Lesson>();
                        var cell = new GridCell
                        {
                            Key = (date, slot.Id),
                            Day = (int)wd,
                            SlotId = slot.Id,
                            Tiles = cellLessons.Select(l => MakeTile(l, lookup, req)).ToList()
                        };
                        cell.IsEmpty = cell.Tiles.Count == 0;
                        cell.IsEmptyHighlight = cell.IsEmpty && req.HighlightEmpty && inPeriod;
                        cell.Hint = inPeriod ? (cell.IsEmpty ? "Пустая ячейка" : cell.Tiles[0].ToolTip) : "Дата вне выбранного периода";
                        row.Cells.Add(cell);
                    }
                    section.Rows.Add(row);
                }
            }
            else
            {
                section.Columns.Add(new GridColumn { Header = "Дата", Key = "date" });
                section.Columns.AddRange(req.Slots.Select(s => new GridColumn { Header = s.Display, Key = s.Id }));
                foreach (var date in dates)
                {
                    var wd = ToWeekDay(date.DayOfWeek);
                    var inPeriod = date >= start && date < end;
                    var row = new GridRow
                    {
                        Header = date.ToString("dd.MM") + " (" + ValidationService.DayName(wd) + ")",
                        Key = date
                    };
                    foreach (var slot in req.Slots)
                    {
                        var cellLessons = inPeriod
                            ? lessons.Where(l => l.TimeSlotId == slot.Id && l.Day == wd).ToList()
                            : new List<Lesson>();
                        var cell = new GridCell
                        {
                            Key = (date, slot.Id),
                            Day = (int)wd,
                            SlotId = slot.Id,
                            Tiles = cellLessons.Select(l => MakeTile(l, lookup, req)).ToList()
                        };
                        cell.IsEmpty = cell.Tiles.Count == 0;
                        cell.IsEmptyHighlight = cell.IsEmpty && req.HighlightEmpty && inPeriod;
                        row.Cells.Add(cell);
                    }
                    section.Rows.Add(row);
                }
            }
            model.Sections.Add(section);
        }

        private static void BuildByEntity(ScheduleGridViewModel model, GridRequest req, List<Lesson> lessons,
            DataLookup lookup, bool isRoom)
        {
            var entityId = isRoom ? req.AuditoriumId : req.TeacherId;
            if (!entityId.HasValue) return;
            var own = lessons.Where(l => isRoom ? l.AuditoriumId == entityId.Value : l.TeacherId == entityId.Value).ToList();
            var start = req.Period.StartDate.Date;
            var end = start.AddDays(req.Period.DurationWeeks * 7);

            var section = new GridSection();

            switch (req.Period.Type)
            {
                case PeriodType.Week:
                    AddSection(model, "", req, own, lookup, req.AllowedDays, DayColumns(req), req.Transposed);
                    return;

                case PeriodType.Month:
                    if (!req.Transposed)
                    {
                        section.Columns.Add(new GridColumn { Header = "Время", Key = "time" });
                        var dates = EnumerateDates(start, end).ToList();
                        foreach (var date in dates)
                            section.Columns.Add(new GridColumn
                            {
                                Header = date.ToString("dd.MM") + "\n" + ValidationService.DayName(ToWeekDay(date.DayOfWeek)),
                                Key = date
                            });
                        foreach (var slot in req.Slots)
                        {
                            var row = new GridRow { Header = slot.Display, Key = slot.Id };
                            foreach (var date in dates)
                            {
                                var wd = ToWeekDay(date.DayOfWeek);
                                var cellLessons = own.Where(l => l.TimeSlotId == slot.Id && l.Day == wd).ToList();
                                var cell = new GridCell
                                {
                                    Key = (date, slot.Id),
                                    Day = (int)wd,
                                    SlotId = slot.Id,
                                    Tiles = cellLessons.Select(l => MakeTile(l, lookup, req)).ToList()
                                };
                                cell.IsEmpty = cell.Tiles.Count == 0;
                                cell.IsEmptyHighlight = cell.IsEmpty && req.HighlightEmpty;
                                cell.Hint = cell.IsEmpty ? "Свободно" : cell.Tiles[0].ToolTip;
                                row.Cells.Add(cell);
                            }
                            section.Rows.Add(row);
                        }
                    }
                    else
                    {
                        section.Columns.Add(new GridColumn { Header = "Дата", Key = "date" });
                        section.Columns.AddRange(req.Slots.Select(s => new GridColumn { Header = s.Display, Key = s.Id }));
                        foreach (var date in EnumerateDates(start, end))
                        {
                            var wd = ToWeekDay(date.DayOfWeek);
                            var row = new GridRow { Header = date.ToString("dd.MM"), Key = date };
                            foreach (var slot in req.Slots)
                            {
                                var cellLessons = own.Where(l => l.TimeSlotId == slot.Id && l.Day == wd).ToList();
                                var cell = new GridCell
                                {
                                    Key = (date, slot.Id),
                                    Day = (int)wd,
                                    SlotId = slot.Id,
                                    Tiles = cellLessons.Select(l => MakeTile(l, lookup, req)).ToList()
                                };
                                cell.IsEmpty = cell.Tiles.Count == 0;
                                row.Cells.Add(cell);
                            }
                            section.Rows.Add(row);
                        }
                    }
                    model.Sections.Add(section);
                    return;

                default: // Семестр: недели как колонки, ячейка агрегирует занятия недели в слоте
                    section.Columns.Add(new GridColumn { Header = "Слот", Key = "slot" });
                    for (var w = 0; w < req.Period.DurationWeeks; w++)
                    {
                        var weekStart = start.AddDays(w * 7);
                        section.Columns.Add(new GridColumn
                        {
                            Header = $"Нед. {w + 1}\n{weekStart:dd.MM}–{weekStart.AddDays(4):dd.MM}",
                            Key = w
                        });
                    }
                    foreach (var slot in req.Slots)
                    {
                        var row = new GridRow { Header = slot.Display, Key = slot.Id };
                        for (var w = 0; w < req.Period.DurationWeeks; w++)
                        {
                            var cell = new GridCell { Key = (w, slot.Id), SlotId = slot.Id };
                            var weekStart = start.AddDays(w * 7);
                            for (var di = 0; di < 6; di++)
                            {
                                var date = weekStart.AddDays(di);
                                if (date < start || date >= end) continue;
                                var wd = ToWeekDay(date.DayOfWeek);
                                foreach (var l in own.Where(x => x.TimeSlotId == slot.Id && x.Day == wd))
                                {
                                    var tile = MakeTile(l, lookup, req);
                                    tile.Line1 = $"{ValidationService.DayName(wd)} · {l.Discipline.Name}";
                                    cell.Tiles.Add(tile);
                                }
                            }
                            cell.IsEmpty = cell.Tiles.Count == 0;
                            cell.IsEmptyHighlight = cell.IsEmpty && req.HighlightEmpty;
                            cell.Hint = cell.IsEmpty ? "Свободно" : string.Join("\n", cell.Tiles.Select(t => t.ToolTip));
                            row.Cells.Add(cell);
                        }
                        section.Rows.Add(row);
                    }
                    model.Sections.Add(section);
                    return;
            }
        }

        private static IEnumerable<DateTime> EnumerateDates(DateTime start, DateTime end)
        {
            var first = new DateTime(start.Year, start.Month, 1);
            var gridStart = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
            for (var d = gridStart; d < end; d = d.AddDays(1)) yield return d;
        }

        private static void BuildSummary(ScheduleGridViewModel model, GridRequest req, List<Lesson> lessons, DataLookup lookup)
        {
            var section = new GridSection();
            var dimHeader = req.SummaryDimension == SummaryDimension.Groups ? "Группа"
                : req.SummaryDimension == SummaryDimension.Teachers ? "Преподаватель" : "Аудитория";
            section.Columns.Add(new GridColumn { Header = dimHeader, Key = "dim" });
            foreach (var day in req.AllowedDays)
                section.Columns.Add(new GridColumn
                {
                    Header = ValidationService.DayName(day),
                    SubHeader = ValidationService.DayFullName(day),
                    Key = (int)day
                });
            section.Columns.Add(new GridColumn { Header = "Итого", Key = "total" });

            IEnumerable<IGrouping<int, Lesson>> dims;
            Func<int, string> label;
            switch (req.SummaryDimension)
            {
                case SummaryDimension.Teachers:
                    dims = lessons.GroupBy(l => l.TeacherId);
                    label = lookup.Teacher;
                    break;
                case SummaryDimension.Auditoriums:
                    dims = lessons.GroupBy(l => l.AuditoriumId);
                    label = id => "Аудитория " + lookup.Room(id);
                    break;
                default:
                    dims = lessons.SelectMany(l => l.LessonGroups.Select(g => new { g.GroupId, Lesson = l }))
                        .GroupBy(x => x.GroupId, x => x.Lesson);
                    label = id => "Группа " + lookup.Group(id);
                    break;
            }

            foreach (var g in dims.OrderBy(x => label(x.Key)))
            {
                var row = new GridRow { Header = label(g.Key), Key = g.Key };
                var total = 0;
                foreach (var day in req.AllowedDays)
                {
                    var dayLessons = g.Where(l => l.Day == day).ToList();
                    total += dayLessons.Count;
                    var cell = new GridCell
                    {
                        Key = (g.Key, (int)day),
                        Day = (int)day,
                        Tiles = dayLessons.Select(l => MakeTile(l, lookup, req)).ToList()
                    };
                    cell.IsEmpty = dayLessons.Count == 0;
                    cell.IsEmptyHighlight = false;
                    cell.Hint = dayLessons.Count == 0 ? "Нет занятий" : string.Join("\n", dayLessons.Select(l => l.Discipline.Name));
                    row.Cells.Add(cell);
                }
                row.Cells.Add(new GridCell
                {
                    Key = (g.Key, "total"),
                    Tiles = new List<LessonTile>(),
                    Hint = $"Всего занятий в неделю: {total}"
                });
                row.Cells[row.Cells.Count - 1].IsEmpty = false;
                row.Cells[row.Cells.Count - 1].IsEmptyHighlight = false;
                section.Rows.Add(row);
            }
            model.Sections.Add(section);
        }

        private static LessonTile MakeTile(Lesson l, DataLookup lookup, GridRequest req)
        {
            var groups = string.Join(", ", l.LessonGroups.Select(g => lookup.Group(g.GroupId)));
            var lines = new List<string> { l.Discipline.Name };
            var tooltip = $"{l.Discipline.Name} ({ValidationService.TypeNameDisc(l.Discipline.Type)})\n" +
                          $"{lookup.Teacher(l.TeacherId)}\nАудитория {lookup.Room(l.AuditoriumId)} · {groups}\n" +
                          $"{ValidationService.DayFullName(l.Day)}, {l.TimeSlot.Display}";
            if (l.IsLocked) tooltip += "\nЗаблокировано";

            return new LessonTile
            {
                LessonId = l.Id,
                Day = (int)l.Day,
                SlotId = l.TimeSlotId,
                RoomId = l.AuditoriumId,
                TeacherId = l.TeacherId,
                DisciplineId = l.DisciplineId,
                GroupIds = l.LessonGroups.Select(g => g.GroupId).ToArray(),
                Line1 = l.Discipline.Name,
                Line2 = (req.ShowTeacher ? lookup.Teacher(l.TeacherId) : "") +
                        (req.ShowRoom ? " · " + lookup.Room(l.AuditoriumId) : ""),
                Line3 = req.ShowGroups ? groups : "",
                ColorHex = l.Discipline.ColorHex ?? "#4E79A7",
                IsLocked = l.IsLocked,
                IsConflict = req.ConflictLessonIds != null && req.ConflictLessonIds.Contains(l.Id),
                ToolTip = tooltip
            };
        }

        private static WeekDay ToWeekDay(DayOfWeek d) => d switch
        {
            DayOfWeek.Monday => WeekDay.Monday,
            DayOfWeek.Tuesday => WeekDay.Tuesday,
            DayOfWeek.Wednesday => WeekDay.Wednesday,
            DayOfWeek.Thursday => WeekDay.Thursday,
            DayOfWeek.Friday => WeekDay.Friday,
            DayOfWeek.Saturday => WeekDay.Saturday,
            _ => WeekDay.Sunday
        };
    }

    public sealed class DataLookup
    {
        private readonly Dictionary<int, string> _rooms = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _teachers = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _groups = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _disciplines = new Dictionary<int, string>();

        public DataLookup(GridRequest req)
        {
            foreach (var kv in req.RoomNames) _rooms[kv.Key] = kv.Value;
            foreach (var kv in req.TeacherNames) _teachers[kv.Key] = kv.Value;
            foreach (var kv in req.GroupNames) _groups[kv.Key] = kv.Value;
            foreach (var kv in req.DisciplineNames) _disciplines[kv.Key] = kv.Value;

            foreach (var l in req.Lessons)
            {
                if (l.Auditorium != null) _rooms[l.AuditoriumId] = l.Auditorium.Number;
                if (l.Teacher != null) _teachers[l.TeacherId] = l.Teacher.FullName;
                if (l.Discipline != null) _disciplines[l.DisciplineId] = l.Discipline.Name;
                foreach (var g in l.LessonGroups)
                    if (g.Group != null) _groups[g.GroupId] = g.Group.Name;
            }
        }

        public string Room(int id) => _rooms.TryGetValue(id, out var v) ? v : $"#{id}";
        public string Teacher(int id) => _teachers.TryGetValue(id, out var v) ? v : $"#{id}";
        public string Group(int id) => _groups.TryGetValue(id, out var v) ? v : $"#{id}";
        public string Discipline(int id) => _disciplines.TryGetValue(id, out var v) ? v : $"#{id}";
    }
}
