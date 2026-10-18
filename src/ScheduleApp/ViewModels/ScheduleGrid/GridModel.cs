using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ScheduleApp.Domain;

namespace ScheduleApp.ViewModels.ScheduleGrid
{
    /// <summary>Модель отображения «шахматки» (полностью UI-независимая, служит также источником для экспорта).</summary>
    public sealed class GridSection
    {
        public string Title { get; set; } = "";
        public List<GridColumn> Columns { get; set; } = new List<GridColumn>();
        public List<GridRow> Rows { get; set; } = new List<GridRow>();
    }

    public sealed class GridColumn
    {
        public string Header { get; set; }
        public string SubHeader { get; set; }
        public object Key { get; set; }
    }

    public sealed class GridRow
    {
        public string Header { get; set; }
        public string SubHeader { get; set; }
        public object Key { get; set; }
        public List<GridCell> Cells { get; set; } = new List<GridCell>();
    }

    public sealed class GridCell
    {
        public object Key { get; set; }
        public int Day { get; set; } = -1;
        public int SlotId { get; set; } = -1;
        public int RoomId { get; set; } = -1;   // контекст аудитории в режиме «по аудитории»
        public bool IsEmpty { get; set; }
        public bool IsEmptyHighlight { get; set; }
        public List<LessonTile> Tiles { get; set; } = new List<LessonTile>();
        public string Hint { get; set; } = "";
    }

    public sealed class LessonTile
    {
        public int LessonId { get; set; }
        public int Day { get; set; }
        public int SlotId { get; set; }
        public int RoomId { get; set; }
        public int TeacherId { get; set; }
        public int DisciplineId { get; set; }
        public int[] GroupIds { get; set; }
        public string Line1 { get; set; }   // дисциплина
        public string Line2 { get; set; }   // преподаватель / аудитория
        public string Line3 { get; set; }   // группы
        public string ColorHex { get; set; } = "#4E79A7";
        public bool IsLocked { get; set; }
        public bool IsConflict { get; set; }
        public string ToolTip { get; set; } = "";
    }

    public sealed class ScheduleGridViewModel
    {
        public ObservableCollection<GridSection> Sections { get; } = new ObservableCollection<GridSection>();
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public bool IsTransposed { get; set; }

        public bool HasLessons =>
            Sections.Any(s => s.Rows.Any(r => r.Cells.Any(c => c.Tiles.Count > 0)));
    }
}
