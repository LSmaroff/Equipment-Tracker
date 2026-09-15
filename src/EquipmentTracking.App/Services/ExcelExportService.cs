using System.IO;
using ClosedXML.Excel;
using ClosedXML.Graphics;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public sealed class ExcelExportService
{
    private static readonly object GraphicEngineSync = new();
    private static bool _graphicEngineConfigured;

    private readonly DatabaseService _database;
    private readonly SettingsService _settings;
    private readonly FileLogger _logger;

    public ExcelExportService(
        DatabaseService database,
        SettingsService settings,
        FileLogger logger)
    {
        _database = database;
        _settings = settings;
        _logger = logger;
    }

    public async Task<string> ExportAsync(CancellationToken cancellationToken = default)
    {
        string? tempPath = null;

        try
        {
            ConfigureClosedXmlGraphicEngine();

            var outputPath = _settings.ResolvePath(_settings.Current.ExcelExportPath);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new InvalidOperationException("The Excel export path is not configured.");
            }

            if (Directory.Exists(outputPath))
            {
                throw new IOException(
                    "The Excel export path points to a folder. Select a complete .xlsx file path in Settings.");
            }

            var outputDirectory = Path.GetDirectoryName(outputPath)
                ?? throw new InvalidOperationException("The Excel export folder is invalid.");
            Directory.CreateDirectory(outputDirectory);

            var snapshot = await _database.GetSnapshotAsync(cancellationToken);
            tempPath = Path.Combine(
                outputDirectory,
                $".{Path.GetFileNameWithoutExtension(outputPath)}-{Guid.NewGuid():N}.xlsx");

            using var workbook = new XLWorkbook();
            CreateTransactionsSheet(workbook, snapshot.Transactions);
            CreateDevicesSheet(workbook, snapshot.Transactions);
            CreateAuditSheet(workbook, snapshot.AuditRecords);
            workbook.SaveAs(tempPath);

            File.Move(tempPath, outputPath, overwrite: true);
            tempPath = null;
            _logger.Information($"Excel export updated: {outputPath}");
            return outputPath;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Error("Excel export failed because access to a required path was denied.", ex);
            throw new IOException(
                "The Excel report could not be written because Windows denied access to a file or folder. " +
                "Confirm the export path is writable and close the workbook if it is open. " +
                "The SQLite record was not lost.",
                ex);
        }
        catch (IOException ex)
        {
            _logger.Error("Excel export failed, possibly because the workbook is open.", ex);
            throw new IOException(
                "The Excel workbook could not be replaced. Close it in Excel and try Export again. " +
                "The SQLite record was not lost.",
                ex);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
        }
    }

    private static void CreateTransactionsSheet(
        XLWorkbook workbook,
        IReadOnlyCollection<EquipmentTransaction> transactions)
    {
        var worksheet = workbook.Worksheets.Add("Transactions");
        var headers = new[]
        {
            "Transaction ID",
            "Customer",
            "Rank",
            "Phone Number",
            "Technician Name, Grade",
            "Organization",
            "Ticket Number",
            "Issued Date",
            "Device Count",
            "PDF Signer Name",
            "Signature Time",
            "Archived",
            "Closed Date",
            "Closeout Technician",
            "PDF Path"
        };

        WriteHeaders(worksheet, headers);

        var row = 2;
        foreach (var transaction in transactions)
        {
            worksheet.Cell(row, 1).Value = transaction.Id;
            worksheet.Cell(row, 2).Value = transaction.Customer.DisplayName;
            worksheet.Cell(row, 3).Value = transaction.Customer.Rank;
            worksheet.Cell(row, 4).Value = transaction.PhoneNumber;
            worksheet.Cell(row, 5).Value = transaction.Technician;
            worksheet.Cell(row, 6).Value = transaction.Organization;
            worksheet.Cell(row, 7).Value = transaction.TicketNumber;
            worksheet.Cell(row, 8).Value = transaction.IssuedAt.LocalDateTime;
            worksheet.Cell(row, 9).Value = transaction.Devices.Count;
            worksheet.Cell(row, 10).Value = transaction.PdfSignerName;
            if (transaction.SignatureTime.HasValue)
            {
                worksheet.Cell(row, 11).Value = transaction.SignatureTime.Value.LocalDateTime;
            }
            worksheet.Cell(row, 12).Value = transaction.IsArchived ? "Yes" : "No";
            if (transaction.ClosedAt.HasValue)
            {
                worksheet.Cell(row, 13).Value = transaction.ClosedAt.Value.LocalDateTime;
            }
            worksheet.Cell(row, 14).Value = transaction.CloseoutTechnician;
            worksheet.Cell(row, 15).Value = transaction.PdfPath;
            row++;
        }

        FormatSheet(worksheet, headers.Length, row - 1, "TransactionsTable");
        SetColumnWidths(
            worksheet,
            24, 28, 12, 16, 24, 18, 16, 18, 12, 24, 18, 12, 18, 24, 60);
        worksheet.Column(8).Style.DateFormat.Format = "mm/dd/yyyy hh:mm";
        worksheet.Column(11).Style.DateFormat.Format = "mm/dd/yyyy hh:mm";
        worksheet.Column(13).Style.DateFormat.Format = "mm/dd/yyyy hh:mm";
    }

    private static void CreateDevicesSheet(
        XLWorkbook workbook,
        IReadOnlyCollection<EquipmentTransaction> transactions)
    {
        var worksheet = workbook.Worksheets.Add("Devices");
        var headers = new[]
        {
            "Device ID",
            "Transaction ID",
            "Ticket Number",
            "Customer",
            "Model Name",
            "Part Number",
            "Serial Number",
            "Asset Tag",
            "Status",
            "Issued Date",
            "Returned Date",
            "Return Condition",
            "Return Notes"
        };

        WriteHeaders(worksheet, headers);

        var row = 2;
        foreach (var transaction in transactions)
        {
            foreach (var device in transaction.Devices)
            {
                worksheet.Cell(row, 1).Value = device.Id;
                worksheet.Cell(row, 2).Value = transaction.Id;
                worksheet.Cell(row, 3).Value = transaction.TicketNumber;
                worksheet.Cell(row, 4).Value = transaction.Customer.DisplayName;
                worksheet.Cell(row, 5).Value = device.Model;
                worksheet.Cell(row, 6).Value = device.PartNumber;
                worksheet.Cell(row, 7).Value = device.SerialNumber;
                worksheet.Cell(row, 8).Value = device.AssetTag;
                worksheet.Cell(row, 9).Value = device.Status;
                worksheet.Cell(row, 10).Value = transaction.IssuedAt.LocalDateTime;
                if (device.ReturnedAt.HasValue)
                {
                    worksheet.Cell(row, 11).Value = device.ReturnedAt.Value.LocalDateTime;
                }
                worksheet.Cell(row, 12).Value = device.ReturnCondition;
                worksheet.Cell(row, 13).Value = device.ReturnNotes;
                row++;
            }
        }

        FormatSheet(worksheet, headers.Length, row - 1, "DevicesTable");
        SetColumnWidths(
            worksheet,
            12, 24, 16, 24, 28, 28, 24, 18, 16, 18, 18, 18, 40);
        worksheet.Column(10).Style.DateFormat.Format = "mm/dd/yyyy hh:mm";
        worksheet.Column(11).Style.DateFormat.Format = "mm/dd/yyyy hh:mm";
    }

    private static void CreateAuditSheet(
        XLWorkbook workbook,
        IReadOnlyCollection<AuditRecord> auditRecords)
    {
        var worksheet = workbook.Worksheets.Add("Audit");
        var headers = new[]
        {
            "Audit ID",
            "Transaction ID",
            "Device ID",
            "Action",
            "Details",
            "Technician",
            "Computer",
            "Action Time"
        };

        WriteHeaders(worksheet, headers);

        var row = 2;
        foreach (var record in auditRecords)
        {
            worksheet.Cell(row, 1).Value = record.Id;
            worksheet.Cell(row, 2).Value = record.TransactionId;
            if (record.DeviceId.HasValue)
            {
                worksheet.Cell(row, 3).Value = record.DeviceId.Value;
            }
            worksheet.Cell(row, 4).Value = record.Action;
            worksheet.Cell(row, 5).Value = record.Details;
            worksheet.Cell(row, 6).Value = record.Technician;
            worksheet.Cell(row, 7).Value = record.ComputerName;
            worksheet.Cell(row, 8).Value = record.ActionTime.LocalDateTime;
            row++;
        }

        FormatSheet(worksheet, headers.Length, row - 1, "AuditTable");
        SetColumnWidths(worksheet, 12, 24, 12, 22, 60, 24, 20, 18);
        worksheet.Column(8).Style.DateFormat.Format = "mm/dd/yyyy hh:mm";
    }

    private static void ConfigureClosedXmlGraphicEngine()
    {
        lock (GraphicEngineSync)
        {
            if (_graphicEngineConfigured)
            {
                return;
            }

            var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var fontDirectory = Path.Combine(windowsDirectory, "Fonts");
            var fallbackFontPath = new[]
                {
                    Path.Combine(fontDirectory, "segoeui.ttf"),
                    Path.Combine(fontDirectory, "arial.ttf"),
                    Path.Combine(fontDirectory, "tahoma.ttf")
                }
                .FirstOrDefault(File.Exists);

            if (string.IsNullOrWhiteSpace(fallbackFontPath))
            {
                throw new FileNotFoundException(
                    "A standard Windows TrueType font could not be found for Excel report generation.");
            }

            using var fallbackFontStream = File.OpenRead(fallbackFontPath);
            LoadOptions.DefaultGraphicEngine =
                DefaultGraphicEngine.CreateOnlyWithFonts(fallbackFontStream);
            _graphicEngineConfigured = true;
        }
    }

    private static void SetColumnWidths(
        IXLWorksheet worksheet,
        params double[] widths)
    {
        for (var index = 0; index < widths.Length; index++)
        {
            worksheet.Column(index + 1).Width = widths[index];
        }
    }

    private static void WriteHeaders(IXLWorksheet worksheet, string[] headers)
    {
        for (var column = 1; column <= headers.Length; column++)
        {
            worksheet.Cell(1, column).Value = headers[column - 1];
        }

        var headerRange = worksheet.Range(1, 1, 1, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private static void FormatSheet(
        IXLWorksheet worksheet,
        int columnCount,
        int lastRow,
        string tableName)
    {
        worksheet.SheetView.FreezeRows(1);
        worksheet.Range(1, 1, Math.Max(lastRow, 1), columnCount)
            .Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        if (lastRow >= 2)
        {
            var table = worksheet.Range(1, 1, lastRow, columnCount).CreateTable(tableName);
            table.Theme = XLTableTheme.TableStyleMedium2;
        }

        // Fixed widths avoid ClosedXML/SixLabors scanning every system-font entry.
        // Some managed Windows images contain inaccessible folders under C:\Windows\Fonts,
        // which can otherwise throw UnauthorizedAccessException during export.
        for (var column = 1; column <= columnCount; column++)
        {
            worksheet.Column(column).Width = 18;
        }

        worksheet.Range(1, 1, Math.Max(lastRow, 1), columnCount)
            .Style.Alignment.WrapText = true;
    }
}
