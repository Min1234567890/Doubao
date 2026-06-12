using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using VehicleInspection.Application.Models;
using VehicleInspection.Application.Security;

namespace VehicleInspection.Application.Services;

public sealed class ExportService
{
    private readonly AuditService _auditService;
    private readonly AuditedAuthorizationService _authorizationService;

    public ExportService(AuditService auditService, AccessControlService accessControlService)
    {
        _auditService = auditService;
        _authorizationService = new AuditedAuthorizationService(accessControlService, auditService);
    }

    public async Task ExportCsvAsync(UserSession session, IEnumerable<InspectionRecord> records, string path, CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.AuthorizeAsync(session, Permission.ExportReports, path, cancellationToken))
        {
            await _auditService.RecordAsync(session, "ExportCsv", path, "Denied", cancellationToken);
            throw new UnauthorizedAccessException("The current role is not authorized to export reports.");
        }

        var builder = new StringBuilder();
        builder.AppendLine("ScanTime,LicensePlate,Status,Lane,Operator,FodAlerts,SystemHealth");

        foreach (var record in records)
        {
            var line = string.Join(',',
                Escape(record.ScanTime.LocalDateTime.ToString("s")),
                Escape(record.LicensePlate),
                Escape(record.Status.ToString()),
                Escape(record.Lane),
                Escape(record.OperatorName),
                Escape(record.FodAlerts.Count.ToString()),
                Escape(record.SystemHealth));
            builder.AppendLine(line);
        }

        await File.WriteAllTextAsync(path, builder.ToString(), Encoding.UTF8, cancellationToken);
        await _auditService.RecordAsync(session, "ExportCsv", path, "Success", cancellationToken);
    }

    public async Task ExportPdfAsync(UserSession session, IEnumerable<InspectionRecord> records, string path, CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.AuthorizeAsync(session, Permission.ExportReports, path, cancellationToken))
        {
            await _auditService.RecordAsync(session, "ExportPdf", path, "Denied", cancellationToken);
            throw new UnauthorizedAccessException("The current role is not authorized to export reports.");
        }

        var recordList = records.ToList();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Segoe UI"));

                page.Header().Element(c => c.Column(col =>
                {
                    col.Item().Text("UVSS Vehicle Inspection Report")
                        .FontSize(18).Bold().FontColor(Colors.Blue.Darken2);
                    col.Item().Text($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm}")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                    col.Item().Text($"Records: {recordList.Count}")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                }));

                page.Content().Element(c => c.Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1.5f);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1.5f);
                        columns.RelativeColumn(1.5f);
                        columns.RelativeColumn(1);
                    });

                    var hdrStyle = TextStyle.Default.FontSize(9).Bold().FontColor(Colors.White);
                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Blue.Darken2).Padding(4).Text("Scan Time").Style(hdrStyle);
                        header.Cell().Background(Colors.Blue.Darken2).Padding(4).Text("Plate").Style(hdrStyle);
                        header.Cell().Background(Colors.Blue.Darken2).Padding(4).Text("Status").Style(hdrStyle);
                        header.Cell().Background(Colors.Blue.Darken2).Padding(4).Text("Lane").Style(hdrStyle);
                        header.Cell().Background(Colors.Blue.Darken2).Padding(4).Text("Operator").Style(hdrStyle);
                        header.Cell().Background(Colors.Blue.Darken2).Padding(4).Text("FOD").Style(hdrStyle);
                    });

                    for (var i = 0; i < recordList.Count; i++)
                    {
                        var record = recordList[i];
                        var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                        table.Cell().Background(bg).Padding(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .Text(record.ScanTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm")).FontSize(9);
                        table.Cell().Background(bg).Padding(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .Text(record.LicensePlate).FontSize(9).Bold();
                        table.Cell().Background(bg).Padding(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .Text(record.Status.ToString()).FontSize(9);
                        table.Cell().Background(bg).Padding(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .Text(record.Lane).FontSize(9);
                        table.Cell().Background(bg).Padding(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .Text(record.OperatorName).FontSize(9);
                        table.Cell().Background(bg).Padding(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .Text(record.FodAlerts.Count > 0 ? $"{record.FodAlerts.Count}" : "—").FontSize(9);
                    }
                }));

                page.Footer().AlignCenter().Text("UVSS Secure Inspection Report — Confidential")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        }).GeneratePdf(path);

        await _auditService.RecordAsync(session, "ExportPdf", path, "Success", cancellationToken);
    }

    public async Task ExportCurrentRecordPdfAsync(
        UserSession session,
        InspectionRecord record,
        string path,
        int sensitivityLevel = 5,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.AuthorizeAsync(session, Permission.ExportReports, path, cancellationToken))
        {
            await _auditService.RecordAsync(session, "ExportCurrentRecordPdf", path, "Denied", cancellationToken);
            throw new UnauthorizedAccessException("The current role is not authorized to export reports.");
        }

        // Download all three images in parallel
        var uvssTask = DownloadImageBytesAsync(record.UnderVehicleImagePath, cancellationToken);
        var vlprTask = DownloadImageBytesAsync(record.LicensePlateImagePath, cancellationToken);
        var xrayTask = DownloadImageBytesAsync(record.XrayImagePath, cancellationToken);

        await Task.WhenAll(uvssTask, vlprTask, xrayTask);

        var uvssBytes = uvssTask.Result;
        var vlprBytes = vlprTask.Result;
        var xrayBytes = xrayTask.Result;

        var scanTime = record.ScanTime.LocalDateTime;
        var roiBoxes = LoadRoiBoxes(sensitivityLevel);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(16);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Segoe UI"));

                // ── Header ──
                page.Header().Element(c => c.Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem(3).Text("UVSS Vehicle Inspection Report — Single Record")
                            .FontSize(18).Bold().FontColor(Colors.Blue.Darken2);
                        row.RelativeItem(1).AlignRight().Text(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm"))
                            .FontSize(9).FontColor(Colors.Grey.Darken1);
                    });

                    col.Item().PaddingTop(18).Row(row =>
                    {
                        row.RelativeItem().DefaultTextStyle(x => x.FontSize(12).FontColor(Colors.Black)).Text(text =>
                        {
                            text.Span("Plate: ").Bold();
                            text.Span(record.LicensePlate);
                        });

                        row.RelativeItem().DefaultTextStyle(x => x.FontSize(12).FontColor(Colors.Black)).Text(text =>
                        {
                            text.Span("Lane: ").Bold();
                            text.Span(record.Lane);
                        });

                        row.RelativeItem().DefaultTextStyle(x => x.FontSize(12).FontColor(Colors.Black)).Text(text =>
                        {
                            text.Span("Operator: ").Bold();
                            text.Span(record.OperatorName);
                        });

                        row.RelativeItem().DefaultTextStyle(x => x.FontSize(12).FontColor(Colors.Black)).Text(text =>
                        {
                            text.Span("Status: ").Bold();
                            text.Span(record.Status.ToString());
                        });
                    });

                    col.Item().LineHorizontal(1).LineColor(Colors.Blue.Darken2);
                }));

                // ── Content: vertically centered, tightly constrained for single page ──
                page.Content().Column(contentCol =>
                {

                    // ── TOP: UVSS image with ROI rectangles ──
                    contentCol.Item().Height(260).Column(uvssCol =>
                    {
                        uvssCol.Item().Text($"UVSS — UNDER-VEHICLE IMAGE  (ROI L1–L{sensitivityLevel})")
                            .FontSize(8).Bold().FontColor(Colors.Blue.Darken2);

                        if (uvssBytes is not null && uvssBytes.Length > 0)
                        {
                            var compositedBytes = CompositeRoiRectangles(uvssBytes, roiBoxes);
                            uvssCol.Item().ExtendVertical().Border(1).BorderColor(Colors.Blue.Darken1).Element(img =>
                            {
                                img.Image(compositedBytes).FitArea();
                            });
                        }
                        else
                        {
                            uvssCol.Item().ExtendVertical().Border(1).BorderColor(Colors.Blue.Darken1)
                                .Background(Colors.Black);
                        }
                    });

                    contentCol.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Blue.Darken2);

                    // ── BOTTOM: VLPR (40%) | X-ray (40%) | info (20%) ──
                    contentCol.Item().Height(150).Row(bottomRow =>
                    {
                        // LEFT 40%: VLPR license plate image
                        bottomRow.RelativeItem(2).PaddingRight(6).Column(vlprCol =>
                        {
                            vlprCol.Item().Text("VLPR — LICENSE PLATE IMAGE")
                                .FontSize(8).Bold().FontColor(Colors.Blue.Darken2);

                            vlprCol.Item().ExtendVertical().Border(1).BorderColor(Colors.Blue.Darken1).Element(img =>
                            {
                                if (vlprBytes is not null && vlprBytes.Length > 0)
                                    img.Image(vlprBytes).FitArea();
                                else
                                    img.Background(Colors.Black);
                            });
                        });

                        // MIDDLE 40%: X-ray image
                        bottomRow.RelativeItem(2).PaddingRight(6).Column(xrayCol =>
                        {
                            xrayCol.Item().Text("X-RAY IMAGE")
                                .FontSize(8).Bold().FontColor(Colors.Blue.Darken2);

                            xrayCol.Item().ExtendVertical().Border(1).BorderColor(Colors.Blue.Darken1).Element(img =>
                            {
                                if (xrayBytes is not null && xrayBytes.Length > 0)
                                    img.Image(xrayBytes).FitArea();
                                else
                                    img.Background(Colors.Black);
                            });
                        });

                        // RIGHT 20%: Scan Info + Notes
                        bottomRow.RelativeItem(1).PaddingLeft(4).Column(infoCol =>
                        {
                            infoCol.Item().Text("SCAN INFO")
                                .FontSize(8).Bold().FontColor(Colors.Blue.Darken2);

                            infoCol.Item().ExtendVertical().Border(1).BorderColor(Colors.Blue.Darken1).Padding(4).Column(infoBox =>
                            {
                                infoBox.Item().Text($"Time: {scanTime:yyyy-MM-dd HH:mm}").FontSize(7);
                                infoBox.Item().Text($"Lane: {record.Lane}").FontSize(7);

                                if (!string.IsNullOrWhiteSpace(record.Notes))
                                {
                                    infoBox.Item().PaddingTop(8).Text("NOTES")
                                        .FontSize(8).Bold().FontColor(Colors.Grey.Darken1);
                                    infoBox.Item().Text(record.Notes).FontSize(7);
                                }
                            });
                        });
                    });
                });

                // ── Footer ──
                page.Footer().AlignCenter().Text("UVSS Secure Inspection Report — Single Record — Confidential")
                    .FontSize(7).FontColor(Colors.Grey.Darken1);
            });
        }).GeneratePdf(path);

        await _auditService.RecordAsync(session, "ExportCurrentRecordPdf", path, "Success", cancellationToken);
    }

    private static List<RoiBoxData> LoadRoiBoxes(int sensitivityLevel)
    {
        var boxes = new List<RoiBoxData>();
        const string roiPath = @"D:\image\transaction\roi1.json";
        if (!File.Exists(roiPath)) return boxes;

        try
        {
            var json = File.ReadAllText(roiPath);
            var lanes = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, RoiBoxJson>>>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (lanes is null) return boxes;

            foreach (var (lk, classes) in lanes)
            {
                if (!int.TryParse(lk.TrimStart('L'), out var lvl)) continue;
                if (lvl > sensitivityLevel) continue;
                foreach (var (_, box) in classes)
                    boxes.Add(new RoiBoxData
                    {
                        Level = lvl - 1, // 0-based for color array
                        X = box.X, Y = box.Y, W = box.W, H = box.H
                    });
            }
        }
        catch
        {
            // Graceful fallback if ROI file is corrupted
        }

        return boxes;
    }

    private static byte[] CompositeRoiRectangles(byte[] imageBytes, List<RoiBoxData> roiBoxes)
    {
        using var ms = new MemoryStream(imageBytes);
        using var bitmap = new Bitmap(ms);

        // Resize to max 2000px on longest side before compositing
        const int maxDim = 2000;
        var imgW = bitmap.Width;
        var imgH = bitmap.Height;
        using var targetBitmap = imgW > maxDim || imgH > maxDim
            ? new Bitmap(bitmap, new System.Drawing.Size(
                imgW > imgH ? maxDim : (int)(imgW * (double)maxDim / imgH),
                imgH > imgW ? maxDim : (int)(imgH * (double)maxDim / imgW)))
            : bitmap;

        if (roiBoxes.Count == 0)
        {
            using var noRoiMs = new MemoryStream();
            targetBitmap.Save(noRoiMs, System.Drawing.Imaging.ImageFormat.Jpeg);
            return noRoiMs.ToArray();
        }

        var bmpW = (double)targetBitmap.Width;
        var bmpH = (double)targetBitmap.Height;
        const double srcW = 8192, srcH = 4096;

        // Calculate placement (same aspect-ratio logic as WPF control)
        var srcAspect = srcW / srcH;
        var imgAspect = bmpW / bmpH;

        double rw, rh, ox, oy;
        if (srcAspect > imgAspect)
        {
            rw = bmpW; rh = bmpW / srcAspect;
            ox = 0; oy = (bmpH - rh) / 2;
        }
        else
        {
            rh = bmpH; rw = bmpH * srcAspect;
            ox = (bmpW - rw) / 2; oy = 0;
        }
        var sx = rw / srcW;
        var sy = rh / srcH;

        using var g = Graphics.FromImage(targetBitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Per-level colors (L1=red, L2=orange, L3=yellow, L4=green, L5=blue)
        var colors = new System.Drawing.Color[]
        {
            System.Drawing.Color.FromArgb(48, 255, 60, 60),   // L1 – semi-transparent red
            System.Drawing.Color.FromArgb(48, 255, 160, 60),  // L2 – semi-transparent orange
            System.Drawing.Color.FromArgb(48, 240, 200, 60),  // L3 – semi-transparent yellow
            System.Drawing.Color.FromArgb(48, 83, 193, 138),  // L4 – semi-transparent green
            System.Drawing.Color.FromArgb(48, 100, 160, 200), // L5 – semi-transparent blue
        };

        var penColors = new System.Drawing.Color[]
        {
            System.Drawing.Color.FromArgb(200, 255, 60, 60),
            System.Drawing.Color.FromArgb(200, 255, 160, 60),
            System.Drawing.Color.FromArgb(200, 240, 200, 60),
            System.Drawing.Color.FromArgb(200, 83, 193, 138),
            System.Drawing.Color.FromArgb(200, 100, 160, 200),
        };

        foreach (var roi in roiBoxes)
        {
            var level = Math.Clamp(roi.Level, 0, 4);
            using var fillBrush = new SolidBrush(colors[level]);
            using var pen = new Pen(penColors[level], Math.Max(1f, (float)(3.0 * bmpW / imgW)));

            var rx = (float)(ox + roi.X * sx);
            var ry = (float)(oy + roi.Y * sy);
            var rw2 = (float)(roi.W * sx);
            var rh2 = (float)(roi.H * sy);

            g.FillRectangle(fillBrush, rx, ry, rw2, rh2);
            g.DrawRectangle(pen, rx, ry, rw2, rh2);
        }

        using var outMs = new MemoryStream();
        targetBitmap.Save(outMs, System.Drawing.Imaging.ImageFormat.Jpeg);
        return outMs.ToArray();
    }

    private static async Task<byte[]?> DownloadImageBytesAsync(string? imageUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return null;

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            return await client.GetByteArrayAsync(imageUrl, cancellationToken);
        }
        catch
        {
            return null; // Graceful degradation for any download failure
        }
    }

    private static string Escape(string value)
    {
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? '"' + value.Replace("\"", "\"\"") + '"'
            : value;
    }

    private sealed class RoiBoxJson
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
    }

    private sealed class RoiBoxData
    {
        public int Level { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public double W { get; init; }
        public double H { get; init; }
    }
}
