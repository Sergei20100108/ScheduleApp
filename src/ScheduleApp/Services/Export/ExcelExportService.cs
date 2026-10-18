using System;
using System.Drawing;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace ScheduleApp.Services.Export
{
    public sealed class ExcelExportService : IExcelExportService
    {
        static ExcelExportService()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        public void Export(ExportGridModel model, string filePath)
        {
            using (var package = new ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add("Расписание");

                ws.Cells[1, 1].Value = model.Title;
                ws.Cells[1, 1].Style.Font.Name = "Segoe UI";
                ws.Cells[1, 1].Style.Font.Size = 14;
                ws.Cells[1, 1].Style.Font.Bold = true;
                ws.Cells[2, 1].Value = model.Subtitle;
                ws.Cells[2, 1].Style.Font.Name = "Segoe UI";
                ws.Cells[2, 1].Style.Font.Size = 10;
                ws.Cells[2, 1].Style.Font.Italic = true;
                ws.Cells[3, 1].Value = $"Сформировано: {model.GeneratedAt}";
                ws.Cells[3, 1].Style.Font.Name = "Segoe UI";
                ws.Cells[3, 1].Style.Font.Size = 9;
                ws.Cells[3, 1].Style.Font.Italic = true;

                var colCount = model.ColumnHeaders.Count;
                const int headerRow = 5;
                var gridBorder = Color.FromArgb(0xC8, 0xCF, 0xD6);

                for (var c = 0; c < colCount; c++)
                {
                    var cell = ws.Cells[headerRow, c + 1];
                    cell.Value = model.ColumnHeaders[c];
                    cell.Style.Font.Name = "Segoe UI";
                    cell.Style.Font.Size = 11;
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(0x3A, 0x4A, 0x5A));
                    cell.Style.Font.Color.SetColor(Color.White);
                    cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                    cell.Style.WrapText = true;
                    cell.Style.Border.BorderAround(ExcelBorderStyle.Thin, gridBorder);
                }

                for (var r = 0; r < model.Rows.Count; r++)
                {
                    var row = model.Rows[r];
                    var excelRow = headerRow + 1 + r;

                    var head = ws.Cells[excelRow, 1];
                    head.Value = row.Header;
                    head.Style.Font.Name = "Segoe UI";
                    head.Style.Font.Size = 10;
                    head.Style.Font.Bold = true;
                    head.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    head.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(0xEC, 0xF1, 0xF5));
                    head.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                    head.Style.Border.BorderAround(ExcelBorderStyle.Thin, gridBorder);

                    for (var c = 0; c < row.Cells.Count && c + 1 < colCount; c++)
                    {
                        var target = ws.Cells[excelRow, c + 2];
                        var tiles = row.Cells[c].Tiles;
                        if (tiles.Count == 0)
                        {
                            target.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            target.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(0xFA, 0xFB, 0xFC));
                        }
                        else
                        {
                            var text = string.Join(Environment.NewLine + "———" + Environment.NewLine,
                                tiles.ConvertAll(t => t.Text.Replace("\n", Environment.NewLine)));
                            target.Value = text;
                            target.Style.WrapText = true;
                            target.Style.VerticalAlignment = ExcelVerticalAlignment.Top;
                            target.Style.Font.Name = "Segoe UI";
                            target.Style.Font.Size = 9;
                            target.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            target.Style.Fill.BackgroundColor.SetColor(ParseColor(tiles[0].ColorHex));
                        }
                        target.Style.Border.BorderAround(ExcelBorderStyle.Thin, gridBorder);
                    }
                }

                ws.Column(1).Width = 18;
                for (var c = 1; c < colCount; c++) ws.Column(c + 1).Width = 32;
                for (var r = 0; r < model.Rows.Count; r++) ws.Row(headerRow + 1 + r).Height = 60;
                ws.Row(headerRow).Height = 26;

                ws.View.FreezePanes(headerRow + 1, 2);
                ws.PrinterSettings.Orientation = eOrientation.Landscape;
                ws.PrinterSettings.FitToPage = true;
                ws.PrinterSettings.FitToWidth = 1;
                ws.PrinterSettings.FitToHeight = 0;

                package.SaveAs(new System.IO.FileInfo(filePath));
            }
        }

        internal static Color ParseColor(string hex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return Color.White;
                hex = hex.TrimStart('#');
                if (hex.Length == 6)
                {
                    return Color.FromArgb(
                        Convert.ToInt32(hex.Substring(4, 2), 16),
                        Convert.ToInt32(hex.Substring(2, 2), 16),
                        Convert.ToInt32(hex.Substring(0, 2), 16));
                }
            }
            catch { }
            return Color.White;
        }
    }
}
