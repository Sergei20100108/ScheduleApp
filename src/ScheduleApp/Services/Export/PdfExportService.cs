using System;
using System.Collections.Generic;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ScheduleApp.Services.Export
{
    public sealed class PdfExportService : IPdfExportService
    {
        static PdfExportService()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public void Export(ExportGridModel model, string filePath)
        {
            Document.Create(doc =>
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(18, Unit.Millimetre);
                    page.DefaultTextStyle(t => t.FontSize(8).FontFamily("Segoe UI"));

                    page.Header().Column(col =>
                    {
                        col.Item().Text(model.Title).FontSize(15).Bold();
                        col.Item().Text(model.Subtitle).FontSize(9).Italic().FontColor(Colors.Grey.Darken1);
                        col.Item().PaddingTop(2).Text($"Сформировано: {model.GeneratedAt}")
                            .FontSize(8).FontColor(Colors.Grey.Medium);
                    });

                    page.Content().PaddingTop(8).Table(table =>
                    {
                        var colCount = model.ColumnHeaders.Count;
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(58);
                            for (var i = 1; i < colCount; i++) columns.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            foreach (var h in model.ColumnHeaders)
                            {
                                header.Cell().Background("#3A4A5A").Padding(3)
                                    .AlignCenter().AlignMiddle()
                                    .Text(h).FontColor("#FFFFFF").Bold().FontSize(8);
                            }
                        });

                        foreach (var row in model.Rows)
                        {
                            row.Cells.Resize(colCount - 1);

                            // Первая ячейка строки — заголовок строки (время / сущность)
                            table.Cell().Border(0.5f).BorderColor("#C8CFD6").Background("#ECF1F5")
                                .Padding(2).AlignMiddle().Text(row.Header).Bold().FontSize(7.5f);

                            foreach (var cell in row.Cells)
                            {
                                if (cell.Tiles.Count == 0)
                                {
                                    table.Cell().Border(0.5f).BorderColor("#C8CFD6").Background("#FAFBFC").Height(38);
                                }
                                else
                                {
                                    var bg = Lighten(cell.Tiles[0].ColorHex, 0.55);
                                    table.Cell().Border(0.5f).BorderColor("#C8CFD6").Background(bg)
                                        .Padding(2).AlignTop()
                                        .Column(inner =>
                                        {
                                            foreach (var tile in cell.Tiles)
                                            {
                                                inner.Item().PaddingBottom(1.5f).Column(t =>
                                                {
                                                    var lines = tile.Text.Split('\n');
                                                    t.Item().Background(tile.ColorHex).Padding(1)
                                                        .Text(lines[0]).FontColor("#FFFFFF").FontSize(6.5f).Bold();
                                                    if (lines.Length > 1)
                                                        t.Item().Text(string.Join("\n", lines.Skip(1)))
                                                            .FontSize(6.5f);
                                                });
                                            }
                                        });
                                }
                            }
                        }
                    });

                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.Span("Страница ").FontSize(7);
                        t.CurrentPageNumber().FontSize(7);
                        t.Span(" из ").FontSize(7);
                        t.TotalPages().FontSize(7);
                    });
                });
            }).GeneratePdf(filePath);
        }

        private static string Lighten(string hex, double amount)
        {
            try
            {
                hex = (hex ?? "#FFFFFF").TrimStart('#');
                if (hex.Length != 6) return "#FFFFFF";
                static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
                var r = Clamp(Convert.ToInt32(hex.Substring(0, 2), 16) + (int)(255 * amount));
                var g = Clamp(Convert.ToInt32(hex.Substring(2, 2), 16) + (int)(255 * amount));
                var b = Clamp(Convert.ToInt32(hex.Substring(4, 2), 16) + (int)(255 * amount));
                return $"#{r:X2}{g:X2}{b:X2}";
            }
            catch
            {
                return "#FFFFFF";
            }
        }
    }

    internal static class ListExtensions
    {
        public static void Resize<T>(this List<T> list, int size)
        {
            while (list.Count < size) list.Add(default);
            if (list.Count > size) list.RemoveRange(size, list.Count - size);
        }
    }
}
