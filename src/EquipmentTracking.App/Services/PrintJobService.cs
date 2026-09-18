using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using EquipmentTracking.App.Infrastructure;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;

namespace EquipmentTracking.App.Services;

public sealed class PrintJobService
{
    private const double LetterWidthPoints = 612d;
    private const double LetterHeightPoints = 792d;
    private const double HalfLetterHeightPoints = 378d;
    private const double GeometryTolerancePoints = 1d;
    private const double StandardTextHorizontalPaddingPoints = 3d;
    private const double StandardTextVerticalPaddingPoints = 1.5d;
    private const double DeviceTextHorizontalPaddingPoints = 3.5d;
    private const double DeviceTextVerticalPaddingPoints = 2.5d;
    private const double PreferredStandardFontSizePoints = 8.5d;
    private const double PreferredLabelFontSizePoints = 10d;
    private const double PreferredTicketFontSizePoints = 10d;
    private const double PreferredQuantityFontSizePoints = 10d;
    private const double MinimumStandardFontSizePoints = 5.5d;
    private const double MinimumLabelFontSizePoints = 7d;
    private const double MinimumTicketFontSizePoints = 6d;
    private const double MinimumQuantityFontSizePoints = 7.5d;
    private const double TextFontSizeStepPoints = 0.5d;
    private const double StandardLineHeightMultiplier = 1.1d;
    private const double DeviceLineHeightMultiplier = 1.12d;
    private const string ReadableTextFontFamily = "Arial";
    private const long MaximumPdfBytes = 100L * 1024L * 1024L;
    private const int PrintAnnotationFlag = 4;
    private const int ReadOnlyAnnotationFlag = 64;
    private const int LockedAnnotationFlag = 128;
    private const int LockedContentsAnnotationFlag = 512;

    private readonly AppPaths _paths;
    private readonly FileLogger _logger;

    public PrintJobService(AppPaths paths, FileLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public string CreateTwoCopyLetterSheet(string sourcePdfPath, string ticketNumber,
        IReadOnlyCollection<string>? returnedFields = null) =>
        CreateReadableCopy(sourcePdfPath, ticketNumber, returnedFields, twoCopies: true);

    public string CreateReadableStatusView(string sourcePdfPath, string ticketNumber,
        IReadOnlyCollection<string> returnedFields) =>
        CreateReadableCopy(sourcePdfPath, ticketNumber, returnedFields, twoCopies: false);

    private string CreateReadableCopy(string sourcePdfPath, string ticketNumber,
        IReadOnlyCollection<string>? returnedFields, bool twoCopies)
    {
        ValidateSourcePath(sourcePdfPath);
        _paths.EnsureDirectories();

        var safeTicket = SanitizeFileNameSegment(ticketNumber);
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var outputName = $"1297-{(twoCopies ? "two-copy" : "status-view")}-{safeTicket}-{timestamp}-{suffix}.pdf";
        var outputPath = Path.Combine(_paths.PrintJobsDirectory, outputName);
        var temporaryPath = Path.Combine(
            _paths.PrintJobsDirectory,
            $".{Path.GetFileNameWithoutExtension(outputName)}-{Guid.NewGuid():N}.tmp.pdf");
        var sourceHashBefore = ComputeSha256(sourcePdfPath);

        try
        {
            int duplicatedAppearanceCount;
            int redrawnTextFieldCount;
            using (var document = PdfReader.Open(sourcePdfPath, PdfDocumentOpenMode.Modify))
            {
                var page = ValidateAndGetSourcePage(document);
                var sourceCrop = page.EffectiveCropBoxReadOnly;
                var verticalShift = sourceCrop.Y1;

                var sourceContent = page.Contents.CreateSingleContent();
                var sourceContentBytes = sourceContent.Stream?.UnfilteredValue;
                if (sourceContentBytes is null || sourceContentBytes.Length == 0)
                {
                    throw new InvalidDataException(
                        "The selected 1297 does not contain readable page content for printing.");
                }

                if (twoCopies)
                {
                    var duplicatedContent = BuildTranslatedContent(sourceContentBytes, -verticalShift);
                    page.Contents.AppendContent().CreateStream(duplicatedContent);
                }
                var composition = DuplicateWidgetAppearances(
                    document,
                    page,
                    sourceCrop,
                    -verticalShift, twoCopies);
                duplicatedAppearanceCount = composition.DuplicatedAppearanceCount;
                redrawnTextFieldCount = composition.TextFields.Count;

                if (duplicatedAppearanceCount == 0 && redrawnTextFieldCount == 0)
                {
                    throw new InvalidDataException(
                        "The selected 1297 does not contain printable form-field appearances. " +
                        "Open it in Adobe, save it, close Adobe, and try again.");
                }

                document.Internals.Catalog.Elements.Remove("/AcroForm");
                document.Internals.Catalog.Elements.Remove("/Perms");
                if (twoCopies) ExpandToLetterPage(page);
                DrawReadableTextFields(page, composition.TextFields, -verticalShift, twoCopies);
                DrawReturnedDeviceMarks(page, composition.TextFields, returnedFields ?? [],
                    -verticalShift, twoCopies);
                document.Info.Subject = "Derived reference copy; signed originals are preserved under Documents.";
                if (twoCopies) DrawCutGuide(page, sourceCrop);
                document.Save(temporaryPath);
            }

            var sourceHashAfter = ComputeSha256(sourcePdfPath);
            if (!string.Equals(sourceHashBefore, sourceHashAfter, StringComparison.Ordinal))
            {
                throw new IOException(
                    "The selected 1297 changed while the print sheet was being created. " +
                    "Close Adobe or finish saving the PDF, then try again.");
            }

            ValidateGeneratedSheet(
                temporaryPath,
                duplicatedAppearanceCount,
                redrawnTextFieldCount, twoCopies);
            File.Move(temporaryPath, outputPath, overwrite: false);
            _logger.Information(
                $"Created {(twoCopies ? "two-copy Letter print job" : "readable status view")} for ticket {safeTicket}: {Path.GetFileName(outputPath)}");
            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"Could not create readable 1297 copy for {Path.GetFileName(sourcePdfPath)}.",
                ex);
            throw;
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public async Task<bool> DeleteTemporaryPrintJobAsync(string printJobPath)
    {
        var validatedPath = ValidateOwnedPrintJobPath(printJobPath);
        Exception? lastError = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                File.Delete(validatedPath);
                _logger.Information(
                    $"Deleted temporary 1297 print job: {Path.GetFileName(validatedPath)}");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                if (attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200d * (attempt + 1)));
                }
            }
        }

        _logger.Warning(
            $"Temporary 1297 print job is still in use and was queued for deletion: " +
            $"{Path.GetFileName(validatedPath)} ({lastError?.GetType().Name ?? "unknown error"}).");
        QueueDeferredDeletion(validatedPath);
        return false;
    }

    private static PdfPage ValidateAndGetSourcePage(PdfDocument document)
    {
        if (document.PageCount != 1)
        {
            throw new InvalidDataException(
                "Two-copy printing requires a one-page 1297 PDF.");
        }

        var page = document.Pages[0];
        if (page.Rotate % 360 != 0)
        {
            throw new InvalidDataException(
                "The selected 1297 has an unsupported page rotation. Save it in the approved orientation and try again.");
        }

        var crop = page.EffectiveCropBoxReadOnly;
        if (!NearlyEqual(crop.Width, LetterWidthPoints) ||
            !NearlyEqual(crop.Height, HalfLetterHeightPoints) ||
            !NearlyEqual(crop.X1, 0d) ||
            !NearlyEqual(crop.X2, LetterWidthPoints) ||
            !NearlyEqual(crop.Y2, LetterHeightPoints))
        {
            throw new InvalidDataException(
                "The selected PDF does not use the approved 8.5 by 5.25 inch 1297 page layout.");
        }

        return page;
    }

    private static byte[] BuildTranslatedContent(byte[] sourceContent, double translateY)
    {
        var prefix = Encoding.ASCII.GetBytes(
            FormattableString.Invariant($"q\n1 0 0 1 0 {translateY:0.###} cm\n"));
        var suffix = Encoding.ASCII.GetBytes("\nQ\n");
        var result = new byte[prefix.Length + sourceContent.Length + suffix.Length];
        Buffer.BlockCopy(prefix, 0, result, 0, prefix.Length);
        Buffer.BlockCopy(sourceContent, 0, result, prefix.Length, sourceContent.Length);
        Buffer.BlockCopy(
            suffix,
            0,
            result,
            prefix.Length + sourceContent.Length,
            suffix.Length);
        return result;
    }

    private static PrintComposition DuplicateWidgetAppearances(
        PdfDocument document,
        PdfPage page,
        PdfRectangle sourceCrop,
        double translateY, bool twoCopies)
    {
        var originalAnnotationCount = page.Annotations.Count;
        var widgetsToRemove = new List<PdfAnnotation>();
        var replacementStamps = new List<PdfRubberStampAnnotation>();
        var textFields = new List<ReadableTextField>();
        var duplicatedCount = 0;

        for (var index = 0; index < originalAnnotationCount; index++)
        {
            var source = page.Annotations[index];
            if (source is null ||
                !string.Equals(
                    source.Elements.GetName("/Subtype"),
                    "/Widget",
                    StringComparison.Ordinal))
            {
                continue;
            }

            widgetsToRemove.Add(source);

            var sourceRectangle = source.Rectangle;
            if (!IsWithin(sourceRectangle, sourceCrop))
            {
                continue;
            }

            var fieldType = FindInheritedName(source, "/FT");
            if (string.Equals(fieldType, "/Tx", StringComparison.Ordinal))
            {
                var fieldName = FindInheritedString(source, "/T");
                var fieldValue = FindInheritedString(source, "/V");
                if (!string.IsNullOrWhiteSpace(fieldValue))
                {
                    textFields.Add(new ReadableTextField(
                        sourceRectangle,
                        fieldName,
                        NormalizePrintValue(fieldName, fieldValue)));
                }

                // Every text field is redrawn into the print-only content layer so
                // the complete form uses one embedded, readable typeface. Signature
                // and other non-text widgets continue to use their exact appearances.
                continue;
            }

            var appearance = FindInheritedItem(source, "/AP");
            if (appearance is null)
            {
                continue;
            }

            var appearanceState = FindInheritedItem(source, "/AS");
            var sourceFlags = source.Elements.GetInteger("/F");
            replacementStamps.Add(CreateAppearanceStamp(
                document,
                sourceRectangle,
                appearance,
                appearanceState,
                sourceFlags,
                translateY: 0d));
            if (twoCopies) replacementStamps.Add(CreateAppearanceStamp(
                document,
                sourceRectangle,
                appearance,
                appearanceState,
                sourceFlags,
                translateY));
            duplicatedCount++;
        }

        foreach (var widget in widgetsToRemove)
        {
            page.Annotations.Remove(widget);
        }

        foreach (var stamp in replacementStamps)
        {
            page.Annotations.Add(stamp);
        }

        return new PrintComposition(duplicatedCount, textFields);
    }

    private static PdfRubberStampAnnotation CreateAppearanceStamp(
        PdfDocument document,
        PdfRectangle sourceRectangle,
        PdfItem appearance,
        PdfItem? appearanceState,
        int sourceFlags,
        double translateY)
    {
        var stamp = new PdfRubberStampAnnotation(document)
        {
            Rectangle = new PdfRectangle(
                new XPoint(sourceRectangle.X1, sourceRectangle.Y1 + translateY),
                new XPoint(sourceRectangle.X2, sourceRectangle.Y2 + translateY))
        };
        stamp.Elements["/AP"] = CloneDirectContainer(appearance);
        if (appearanceState is not null)
        {
            stamp.Elements["/AS"] = appearanceState;
        }

        stamp.Elements.SetInteger(
            "/F",
            sourceFlags |
            PrintAnnotationFlag |
            ReadOnlyAnnotationFlag |
            LockedAnnotationFlag |
            LockedContentsAnnotationFlag);
        return stamp;
    }

    private static PdfItem? FindInheritedItem(PdfDictionary dictionary, string key)
    {
        PdfDictionary? current = dictionary;
        for (var depth = 0; current is not null && depth < 16; depth++)
        {
            var item = current.Elements[key];
            if (item is not null)
            {
                return item;
            }

            current = current.Elements["/Parent"] switch
            {
                PdfReference reference => reference.Value as PdfDictionary,
                PdfDictionary parent => parent,
                _ => null
            };
        }

        return null;
    }

    private static string FindInheritedName(PdfDictionary dictionary, string key)
    {
        PdfDictionary? current = dictionary;
        for (var depth = 0; current is not null && depth < 16; depth++)
        {
            if (current.Elements[key] is not null)
            {
                return current.Elements.GetName(key);
            }

            current = GetParentDictionary(current);
        }

        return string.Empty;
    }

    private static string FindInheritedString(PdfDictionary dictionary, string key)
    {
        PdfDictionary? current = dictionary;
        for (var depth = 0; current is not null && depth < 16; depth++)
        {
            var item = current.Elements[key];
            if (item is not null)
            {
                return item switch
                {
                    PdfString value => value.Value,
                    PdfStringObject value => value.Value,
                    PdfReference { Value: PdfStringObject value } => value.Value,
                    _ => string.Empty
                };
            }

            current = GetParentDictionary(current);
        }

        return string.Empty;
    }

    private static PdfDictionary? GetParentDictionary(PdfDictionary dictionary)
    {
        return dictionary.Elements["/Parent"] switch
        {
            PdfReference reference => reference.Value as PdfDictionary,
            PdfDictionary parent => parent,
            _ => null
        };
    }

    private static string NormalizePrintValue(string fieldName, string fieldValue)
    {
        var normalized = fieldValue
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        if (!IsDeviceField(fieldName, normalized))
        {
            return CollapseWhitespace(normalized);
        }

        // Older and viewer-regenerated appearances can visually run these
        // labeled values together even when the canonical value is usable.
        // Re-establish one organized paragraph per device identifier.
        foreach (var label in new[] { "Model Name:", "Part Number:", "Serial Number:", "Asset Tag:" })
        {
            normalized = normalized.Replace(
                label,
                $"\n{label}",
                StringComparison.OrdinalIgnoreCase);
        }

        return string.Join(
            "\n",
            normalized
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(CollapseWhitespace)
                .Where(line => line.Length > 0));
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static bool IsDeviceField(string fieldName, string fieldValue)
    {
        if (fieldName.StartsWith("Device", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(fieldName.AsSpan("Device".Length), out _))
        {
            return true;
        }

        return fieldValue.Contains("Model Name:", StringComparison.OrdinalIgnoreCase) ||
               fieldValue.Contains("Part Number:", StringComparison.OrdinalIgnoreCase) ||
               fieldValue.Contains("Serial Number:", StringComparison.OrdinalIgnoreCase) ||
               fieldValue.Contains("Asset Tag:", StringComparison.OrdinalIgnoreCase);
    }

    private static PdfItem CloneDirectContainer(PdfItem item)
    {
        return item switch
        {
            PdfDictionary dictionary => dictionary.Clone(),
            PdfArray array => array.Clone(),
            _ => item
        };
    }

    private static bool IsWithin(PdfRectangle rectangle, PdfRectangle bounds)
    {
        return rectangle.X1 >= bounds.X1 - GeometryTolerancePoints &&
               rectangle.X2 <= bounds.X2 + GeometryTolerancePoints &&
               rectangle.Y1 >= bounds.Y1 - GeometryTolerancePoints &&
               rectangle.Y2 <= bounds.Y2 + GeometryTolerancePoints;
    }

    private static void ExpandToLetterPage(PdfPage page)
    {
        page.MediaBox = CreateLetterRectangle();
        page.CropBox = CreateLetterRectangle();
        page.BleedBox = CreateLetterRectangle();
        page.TrimBox = CreateLetterRectangle();
        page.ArtBox = CreateLetterRectangle();
    }

    private static PdfRectangle CreateLetterRectangle()
    {
        return new PdfRectangle(
            new XPoint(0d, 0d),
            new XPoint(LetterWidthPoints, LetterHeightPoints));
    }

    private static void DrawReadableTextFields(
        PdfPage page,
        IReadOnlyList<ReadableTextField> textFields,
        double bottomCopyTranslation, bool twoCopies)
    {
        if (textFields.Count == 0)
        {
            return;
        }

        using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        foreach (var field in textFields)
        {
            DrawReadableTextField(graphics, field, translateY: 0d);
            if (twoCopies) DrawReadableTextField(graphics, field, bottomCopyTranslation);
        }
    }

    private static void DrawReturnedDeviceMarks(PdfPage page,
        IReadOnlyList<ReadableTextField> textFields, IReadOnlyCollection<string> returnedFields,
        double bottomCopyTranslation, bool twoCopies)
    {
        var names = returnedFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fields = textFields.Where(field => names.Contains(field.FieldName)).ToArray();
        if (fields.Select(field => field.FieldName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
            throw new InvalidDataException("A returned device field is missing or empty in the selected PDF. Check the device field mappings before viewing or printing.");
        page.Elements.SetString("/ETPReturnedFields", string.Join(",", names.Order(StringComparer.OrdinalIgnoreCase)));
        if (fields.Length == 0) return;

        // Draw last, above the readable text, on both copies. Explicit committed device
        // identities also support receipts produced before this presentation feature.
        using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var pen = new XPen(XColor.FromArgb(176, 32, 32), 1.8);
        foreach (var field in fields)
        {
            var rectangle = field.Rectangle;
            var y = LetterHeightPoints - (rectangle.Y1 + rectangle.Y2) / 2d;
            graphics.DrawLine(pen, rectangle.X1 + 2, y, rectangle.X2 - 2, y);
            if (twoCopies)
                graphics.DrawLine(pen, rectangle.X1 + 2, y - bottomCopyTranslation,
                    rectangle.X2 - 2, y - bottomCopyTranslation);
        }
    }

    private static void DrawReadableTextField(
        XGraphics graphics,
        ReadableTextField field,
        double translateY)
    {
        var style = ResolveTextStyle(field);
        var sourceRectangle = field.Rectangle;
        var left = sourceRectangle.X1 + style.HorizontalPaddingPoints;
        var top = LetterHeightPoints - (sourceRectangle.Y2 + translateY) +
                  style.VerticalPaddingPoints;
        var width = sourceRectangle.Width - (style.HorizontalPaddingPoints * 2d);
        var height = sourceRectangle.Height - (style.VerticalPaddingPoints * 2d);
        if (width <= 0d || height <= 0d)
        {
            throw new InvalidDataException(
                $"The PDF text field '{GetSafeFieldLabel(field.FieldName)}' has invalid print geometry.");
        }

        var fontSize = style.PreferredFontSizePoints;
        XFont? selectedFont = null;
        IReadOnlyList<string> selectedLines = [];
        while (fontSize >= style.MinimumFontSizePoints - 0.01d)
        {
            var candidateFont = new XFont(
                ReadableTextFontFamily,
                fontSize,
                XFontStyleEx.Bold,
                new XPdfFontOptions(PdfFontEncoding.Unicode, PdfFontEmbedding.TryComputeSubset));
            var candidateLines = WrapText(graphics, field.Value, candidateFont, width);
            var lineHeight = candidateFont.Size * style.LineHeightMultiplier;
            selectedFont = candidateFont;
            selectedLines = candidateLines;
            if (candidateLines.Count * lineHeight <= height + GeometryTolerancePoints)
            {
                break;
            }

            fontSize -= TextFontSizeStepPoints;
        }

        if (selectedFont is null || selectedLines.Count == 0)
        {
            return;
        }

        var selectedLineHeight = selectedFont.Size * style.LineHeightMultiplier;
        if (selectedLines.Count * selectedLineHeight > height + GeometryTolerancePoints)
        {
            throw new InvalidDataException(
                $"The value in PDF field '{GetSafeFieldLabel(field.FieldName)}' is too long to fit " +
                "legibly inside its approved 1297 box. Shorten that entry and try again.");
        }

        var textHeight = selectedLines.Count * selectedLineHeight;
        var currentY = top + Math.Max(0d, (height - textHeight) / 2d);
        foreach (var line in selectedLines)
        {
            var measuredWidth = graphics.MeasureString(line, selectedFont).Width;
            var lineLeft = style.Alignment switch
            {
                PrintTextAlignment.Center => left + Math.Max(0d, (width - measuredWidth) / 2d),
                PrintTextAlignment.Right => left + Math.Max(0d, width - measuredWidth),
                _ => left
            };
            graphics.DrawString(
                line,
                selectedFont,
                XBrushes.Black,
                new XRect(lineLeft, currentY, Math.Max(1d, width), selectedLineHeight),
                XStringFormats.TopLeft);
            currentY += selectedLineHeight;
        }
    }

    private static PrintTextStyle ResolveTextStyle(ReadableTextField field)
    {
        var name = field.FieldName.Trim();
        if (IsDeviceField(name, field.Value))
        {
            return new PrintTextStyle(
                PreferredStandardFontSizePoints,
                MinimumStandardFontSizePoints,
                DeviceTextHorizontalPaddingPoints,
                DeviceTextVerticalPaddingPoints,
                DeviceLineHeightMultiplier,
                PrintTextAlignment.Left);
        }

        if (IsFieldName(name, "STOCK NUMBERRow1") ||
            IsFieldName(name, "STOCK NUMBERRow4") ||
            IsFieldName(name, "Pickup Signature Text"))
        {
            return new PrintTextStyle(
                PreferredLabelFontSizePoints,
                MinimumLabelFontSizePoints,
                StandardTextHorizontalPaddingPoints,
                StandardTextVerticalPaddingPoints,
                StandardLineHeightMultiplier,
                PrintTextAlignment.Center);
        }

        if (IsFieldName(name, "TicketNumber"))
        {
            return new PrintTextStyle(
                PreferredTicketFontSizePoints,
                MinimumTicketFontSizePoints,
                StandardTextHorizontalPaddingPoints,
                StandardTextVerticalPaddingPoints,
                StandardLineHeightMultiplier,
                PrintTextAlignment.Center);
        }

        if (IsFieldName(name, "QNTY") || IsFieldName(name, "Quantity"))
        {
            return new PrintTextStyle(
                PreferredQuantityFontSizePoints,
                MinimumQuantityFontSizePoints,
                2d,
                StandardTextVerticalPaddingPoints,
                StandardLineHeightMultiplier,
                PrintTextAlignment.Center);
        }

        if (name.StartsWith("UIRow", StringComparison.OrdinalIgnoreCase) ||
            IsFieldName(name, "UI") ||
            IsFieldName(name, "U/I"))
        {
            return new PrintTextStyle(
                PreferredStandardFontSizePoints,
                MinimumStandardFontSizePoints,
                2d,
                StandardTextVerticalPaddingPoints,
                StandardLineHeightMultiplier,
                PrintTextAlignment.Center);
        }

        if (IsFieldName(name, "DATE OF ISSUE") ||
            IsFieldName(name, "RETURN DATE") ||
            name.Contains("DATE", StringComparison.OrdinalIgnoreCase))
        {
            return new PrintTextStyle(
                PreferredStandardFontSizePoints,
                MinimumStandardFontSizePoints,
                StandardTextHorizontalPaddingPoints,
                StandardTextVerticalPaddingPoints,
                StandardLineHeightMultiplier,
                PrintTextAlignment.Center);
        }

        return new PrintTextStyle(
            PreferredStandardFontSizePoints,
            MinimumStandardFontSizePoints,
            StandardTextHorizontalPaddingPoints,
            StandardTextVerticalPaddingPoints,
            StandardLineHeightMultiplier,
            PrintTextAlignment.Left);
    }

    private static bool IsFieldName(string value, string expected)
    {
        return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSafeFieldLabel(string fieldName)
    {
        var compact = CollapseWhitespace(fieldName);
        if (compact.Length == 0)
        {
            return "unnamed text field";
        }

        return compact.Length <= 80 ? compact : compact[..80];
    }

    private static IReadOnlyList<string> WrapText(
        XGraphics graphics,
        string value,
        XFont font,
        double maximumWidth)
    {
        var lines = new List<string>();
        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        foreach (var paragraph in normalized.Split('\n'))
        {
            WrapParagraph(graphics, paragraph.Trim(), font, maximumWidth, lines);
        }

        return lines;
    }

    private static void WrapParagraph(
        XGraphics graphics,
        string paragraph,
        XFont font,
        double maximumWidth,
        ICollection<string> output)
    {
        if (paragraph.Length == 0)
        {
            output.Add(string.Empty);
            return;
        }

        var start = 0;
        while (start < paragraph.Length)
        {
            while (start < paragraph.Length && char.IsWhiteSpace(paragraph[start]))
            {
                start++;
            }

            if (start >= paragraph.Length)
            {
                break;
            }

            var end = start;
            var lastBreak = -1;
            while (end < paragraph.Length)
            {
                var character = paragraph[end];
                var candidate = paragraph[start..(end + 1)];
                if (graphics.MeasureString(candidate, font).Width > maximumWidth)
                {
                    break;
                }

                end++;
                if (char.IsWhiteSpace(character) || character is '-' or '/' or '\\')
                {
                    lastBreak = end;
                }
            }

            if (end >= paragraph.Length)
            {
                output.Add(paragraph[start..].TrimEnd());
                break;
            }

            var lineEnd = lastBreak > start ? lastBreak : Math.Max(start + 1, end);
            output.Add(paragraph[start..lineEnd].TrimEnd());
            start = lineEnd;
        }
    }

    private static void DrawCutGuide(PdfPage page, PdfRectangle sourceCrop)
    {
        var bottomCopyTop = sourceCrop.Height;
        var guidePdfY = (bottomCopyTop + sourceCrop.Y1) / 2d;
        var guideDrawingY = LetterHeightPoints - guidePdfY;
        using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var pen = new XPen(XColor.FromArgb(145, 120, 120, 120), 0.6)
        {
            DashStyle = XDashStyle.Dash
        };
        graphics.DrawLine(pen, 36d, guideDrawingY, LetterWidthPoints - 36d, guideDrawingY);
    }

    private static void ValidateGeneratedSheet(
        string path,
        int expectedStampCount,
        int expectedRedrawnTextFieldCount, bool twoCopies)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
        if (document.PageCount != 1)
        {
            throw new InvalidDataException("The generated print job is not a one-page PDF.");
        }

        var page = document.Pages[0];
        var crop = page.EffectiveCropBoxReadOnly;
        if (!NearlyEqual(crop.Width, LetterWidthPoints) ||
            !NearlyEqual(crop.Height, twoCopies ? LetterHeightPoints : HalfLetterHeightPoints))
        {
            throw new InvalidDataException("The generated 1297 copy does not have the expected page size.");
        }

        if (document.Internals.Catalog.Elements["/AcroForm"] is not null)
        {
            throw new InvalidDataException(
                "The generated print job unexpectedly contains an interactive form field tree.");
        }

        var topStampCount = 0;
        var bottomStampCount = 0;
        for (var index = 0; index < page.Annotations.Count; index++)
        {
            var annotation = page.Annotations[index];
            if (annotation is null)
            {
                continue;
            }

            if (string.Equals(
                    annotation.Elements.GetName("/Subtype"),
                    "/Widget",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The generated print job unexpectedly contains an interactive form widget.");
            }

            if (!string.Equals(
                    annotation.Elements.GetName("/Subtype"),
                    "/Stamp",
                    StringComparison.Ordinal) ||
                (annotation.Elements.GetInteger("/F") & PrintAnnotationFlag) == 0)
            {
                continue;
            }

            if (annotation.Rectangle.Y1 >= -GeometryTolerancePoints &&
                annotation.Rectangle.Y2 <= HalfLetterHeightPoints + GeometryTolerancePoints)
            {
                bottomStampCount++;
            }
            else if (annotation.Rectangle.Y1 >= LetterHeightPoints - HalfLetterHeightPoints - GeometryTolerancePoints &&
                     annotation.Rectangle.Y2 <= LetterHeightPoints + GeometryTolerancePoints)
            {
                topStampCount++;
            }
        }

        if (topStampCount != expectedStampCount || bottomStampCount != (twoCopies ? expectedStampCount : 0))
        {
            throw new InvalidDataException(
                "The generated print job did not preserve every printable field appearance.");
        }

        if (expectedRedrawnTextFieldCount > 0 && page.Contents.Elements.Count < 2)
        {
            throw new InvalidDataException(
                "The generated print job did not include the readable text layer.");
        }
    }

    private static void ValidateSourcePath(string sourcePdfPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePdfPath))
        {
            throw new ArgumentException("A 1297 PDF path is required.", nameof(sourcePdfPath));
        }

        if (!File.Exists(sourcePdfPath))
        {
            throw new FileNotFoundException("The selected 1297 PDF could not be found.", sourcePdfPath);
        }

        var file = new FileInfo(sourcePdfPath);
        if (file.Length <= 0 || file.Length > MaximumPdfBytes)
        {
            throw new InvalidDataException(
                "The selected 1297 PDF is empty or exceeds the 100 MB safety limit.");
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private string ValidateOwnedPrintJobPath(string printJobPath)
    {
        if (string.IsNullOrWhiteSpace(printJobPath))
        {
            throw new ArgumentException("A temporary print-job path is required.", nameof(printJobPath));
        }

        var printDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(_paths.PrintJobsDirectory));
        var fullPath = Path.GetFullPath(printJobPath);
        var requiredPrefix = printDirectory + Path.DirectorySeparatorChar;
        var fileName = Path.GetFileName(fullPath);
        if (!fullPath.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase) ||
            !(fileName.StartsWith("1297-two-copy-", StringComparison.OrdinalIgnoreCase) ||
              fileName.StartsWith("1297-status-view-", StringComparison.OrdinalIgnoreCase)) ||
            !string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only temporary 1297 print jobs created by this application can be deleted here.");
        }

        return fullPath;
    }

    private void QueueDeferredDeletion(string printJobPath)
    {
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 24; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                try
                {
                    File.Delete(printJobPath);
                    _logger.Information(
                        $"Deleted released temporary 1297 print job: {Path.GetFileName(printJobPath)}");
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The PDF viewer still has the temporary derivative open.
                }
                catch (Exception ex)
                {
                    _logger.Warning(
                        $"Deferred temporary print-job deletion stopped: {ex.GetType().Name}.");
                    return;
                }
            }

            _logger.Warning(
                $"Temporary 1297 print job remained locked and will be removed by startup maintenance: " +
                Path.GetFileName(printJobPath));
        });
    }

    private static string SanitizeFileNameSegment(string value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "untitled" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(Math.Min(source.Length, 64));
        foreach (var character in source)
        {
            if (builder.Length >= 64)
            {
                break;
            }

            builder.Append(invalid.Contains(character) || char.IsControl(character) ? '_' : character);
        }

        var result = builder.ToString().Trim().TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(result) ? "untitled" : result;
    }

    private static bool NearlyEqual(double first, double second)
    {
        return Math.Abs(first - second) <= GeometryTolerancePoints;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup; startup maintenance handles abandoned temp files.
        }
    }

    private enum PrintTextAlignment
    {
        Left,
        Center,
        Right
    }

    private sealed record PrintTextStyle(
        double PreferredFontSizePoints,
        double MinimumFontSizePoints,
        double HorizontalPaddingPoints,
        double VerticalPaddingPoints,
        double LineHeightMultiplier,
        PrintTextAlignment Alignment);

    private sealed record ReadableTextField(
        PdfRectangle Rectangle,
        string FieldName,
        string Value);

    private sealed record PrintComposition(
        int DuplicatedAppearanceCount,
        IReadOnlyList<ReadableTextField> TextFields);
}
