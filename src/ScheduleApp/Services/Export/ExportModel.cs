using System.Collections.Generic;

namespace ScheduleApp.Services.Export
{
    /// <summary>Независимая от UI-модель «шахматки» для экспорта.</summary>
    public sealed class ExportGridModel
    {
        public string Title = "Расписание занятий";
        public string Subtitle = "";
        public string GeneratedAt = "";
        public List<string> ColumnHeaders = new List<string>();
        public List<ExportRow> Rows = new List<ExportRow>();
    }

    public sealed class ExportRow
    {
        public string Header = "";
        public List<ExportCell> Cells = new List<ExportCell>();
    }

    public sealed class ExportCell
    {
        public List<ExportTile> Tiles = new List<ExportTile>();
    }

    public sealed class ExportTile
    {
        public string Text = "";
        public string ColorHex = "#FFFFFF";
    }

    public interface IExcelExportService
    {
        void Export(ExportGridModel model, string filePath);
    }

    public interface IPdfExportService
    {
        void Export(ExportGridModel model, string filePath);
    }
}
