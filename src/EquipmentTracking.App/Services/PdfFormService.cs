using System.IO;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;

namespace EquipmentTracking.App.Services;

public sealed class PdfFormService
{
    private readonly FileLogger _logger;

    public PdfFormService(FileLogger logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<string> ListFieldNames(string pdfPath)
    {
        ValidatePdfPath(pdfPath);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        if (document.Internals.Catalog.Elements["/AcroForm"] is null)
        {
            return [];
        }

        var acroForm = document.AcroForm;
        var names = new List<string>();
        CollectFieldNames(acroForm.Fields, parentName: string.Empty, names);

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public PdfTemplateInspection InspectTemplate(
        string pdfPath,
        IReadOnlyCollection<string>? requiredFields = null)
    {
        ValidatePdfPath(pdfPath);
        var fieldNames = ListFieldNames(pdfPath);
        var required = requiredFields ?? [];
        var missing = required
            .Where(name => !fieldNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var needAppearances = document.AcroForm?.Elements.GetBoolean("/NeedAppearances") ?? false;
        var signatureCount = document.AcroForm is null
            ? 0
            : CountSignatureFields(document.AcroForm.Fields);
        var rawText = File.ReadAllText(pdfPath, System.Text.Encoding.Latin1);
        var containsJavaScript =
            rawText.Contains("/JavaScript", StringComparison.Ordinal) ||
            rawText.Contains("/JavaScript ", StringComparison.Ordinal) ||
            rawText.Contains("/JS ", StringComparison.Ordinal);

        return new PdfTemplateInspection
        {
            PdfPath = pdfPath,
            FieldNames = fieldNames,
            SignatureFieldCount = signatureCount,
            NeedAppearances = needAppearances,
            ContainsJavaScriptMarkers = containsJavaScript,
            MissingRequiredFields = missing
        };
    }

    public PdfFillResult UpdateLogicalFieldsInPlace(
        string pdfPath,
        IReadOnlyDictionary<string, string> logicalValues,
        AppSettings settings)
    {
        return FillFields(pdfPath, pdfPath, logicalValues, settings);
    }

    public PdfFillResult FillFields(
        string inputPath,
        string outputPath,
        IReadOnlyDictionary<string, string> logicalValues,
        AppSettings settings,
        string? recordId = null)
    {
        ValidatePdfPath(inputPath);
        ArgumentNullException.ThrowIfNull(logicalValues);
        ArgumentNullException.ThrowIfNull(settings);

        var configuredMappings = settings.PdfFieldMappings
            .Where(pair =>
                !string.IsNullOrWhiteSpace(pair.Key) &&
                !string.IsNullOrWhiteSpace(pair.Value) &&
                logicalValues.ContainsKey(pair.Key))
            .ToArray();

        if (configuredMappings.Length == 0)
        {
            throw new InvalidOperationException(
                "No usable PDF field mappings are configured. " +
                "Use Settings → Inspect PDF fields and map the required fields.");
        }

        var outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("The output PDF folder is invalid.");
        Directory.CreateDirectory(outputDirectory);

        var tempPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileNameWithoutExtension(outputPath)}-{Guid.NewGuid():N}.pdf");

        var missingFields = new List<string>();
        var filledCount = 0;

        try
        {
            using (var document = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify))
            {
                if (document.AcroForm is null || document.AcroForm.Fields.Count == 0)
                {
                    var rawText = File.ReadAllText(inputPath, System.Text.Encoding.Latin1);
                    var formType = rawText.Contains("/XFA", StringComparison.Ordinal)
                        ? "The template appears to be an XFA form, which this alpha cannot fill."
                        : "The template does not contain readable AcroForm fields.";

                    throw new InvalidOperationException(formType);
                }

                document.AcroForm.Elements.SetBoolean("/NeedAppearances", false);

                // Some valid AcroForms store the field value in a parent field while
                // the visible widget rectangle lives only in a single child widget.
                // PDFsharp 6.2.x renders a text field appearance from the field's own
                // /Rect entry, so copy the widget geometry to the parent before any
                // value is assigned. This keeps /NeedAppearances=false and avoids
                // depending on Adobe to regenerate field appearances.
                PrepareTextFieldsForAppearance(document.AcroForm.Fields);

                foreach (var mapping in configuredMappings)
                {
                    var value = logicalValues[mapping.Key] ?? string.Empty;
                    var field = FindField(
                        document.AcroForm.Fields,
                        mapping.Value,
                        parentName: string.Empty);

                    if (field is null)
                    {
                        missingFields.Add(mapping.Value);
                        continue;
                    }

                    SetFieldValue(field, value);
                    filledCount++;
                }

                using var recordCodeImage = string.IsNullOrWhiteSpace(recordId)
                    ? null
                    : CreateRecordCodeImage(recordId);
                if (recordCodeImage is not null)
                {
                    DrawRecordIdentity(document, recordId!, recordCodeImage.Image);
                }

                document.Save(tempPath);
            }

            File.Move(tempPath, outputPath, overwrite: true);
            _logger.Information($"Filled {filledCount} PDF fields in {outputPath}");

            return new PdfFillResult
            {
                ConfiguredFieldCount = configuredMappings.Length,
                FieldsFilled = filledCount,
                MissingPdfFields = missingFields
            };
        }
        finally
        {
            if (File.Exists(tempPath))
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

    private static RecordCodeImageResources CreateRecordCodeImage(string recordId)
    {
        var qrBytes = new RecordCodeService().GeneratePng(recordId);
        var stream = new MemoryStream(
            qrBytes,
            0,
            qrBytes.Length,
            writable: false,
            publiclyVisible: true);
        try
        {
            return new RecordCodeImageResources(stream, XImage.FromStream(stream));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void DrawRecordIdentity(
        PdfDocument document,
        string recordId,
        XImage qrImage)
    {
        if (document.PageCount == 0)
        {
            throw new InvalidDataException("The 1297 PDF does not contain a page for the record code.");
        }

        using var graphics = XGraphics.FromPdfPage(
            document.Pages[0],
            XGraphicsPdfPageOptions.Append);

        // The approved half-page 1297 has an unused footer gap in these coordinates.
        // Keep the code small enough to preserve the printed form while retaining a
        // quiet zone for ordinary USB QR scanners.
        const double qrX = 392d;
        const double qrY = 336d;
        const double qrSize = 38d;
        graphics.DrawImage(qrImage, qrX, qrY, qrSize, qrSize);

        var labelFont = new XFont("Arial", 5.2d, XFontStyleEx.Bold);
        var valueFont = new XFont("Arial", 4.8d, XFontStyleEx.Regular);
        graphics.DrawString("1297 ID", labelFont, XBrushes.Black, new XPoint(164d, 344d));
        graphics.DrawString(recordId, valueFont, XBrushes.Black, new XPoint(164d, 352d));
    }

    private sealed class RecordCodeImageResources : IDisposable
    {
        private readonly Stream _stream;

        public RecordCodeImageResources(Stream stream, XImage image)
        {
            _stream = stream;
            Image = image;
        }

        public XImage Image { get; }

        public void Dispose()
        {
            Image.Dispose();
            _stream.Dispose();
        }
    }

    public PdfFillResult CreatePartialPickupPdf(
        string originalSignedPdfPath,
        string outputPath,
        IReadOnlyCollection<string> logicalDeviceFieldNames,
        DateTimeOffset pickupStartedAt,
        AppSettings settings)
    {
        ValidatePdfPath(originalSignedPdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(logicalDeviceFieldNames);
        ArgumentNullException.ThrowIfNull(settings);

        var sourcePath = Path.GetFullPath(originalSignedPdfPath);
        var destinationPath = Path.GetFullPath(outputPath);
        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "A partial-pickup receipt must be a new child PDF; the signed original cannot be modified.");
        }
        if (File.Exists(destinationPath))
        {
            throw new IOException(
                $"The partial-pickup receipt already exists: {destinationPath}");
        }

        var logicalFields = logicalDeviceFieldNames
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (logicalFields.Length == 0)
        {
            throw new InvalidOperationException(
                "Select at least one device for the partial-pickup receipt.");
        }

        if (!settings.PdfFieldMappings.TryGetValue(
                "ReturnDate",
                out var mappedReturnDateField) ||
            string.IsNullOrWhiteSpace(mappedReturnDateField))
        {
            throw new InvalidOperationException(
                "The PDF field mapping for 'ReturnDate' is missing.");
        }
        mappedReturnDateField = mappedReturnDateField.Trim();

        var mappedFields = new List<string>(logicalFields.Length);
        foreach (var logicalField in logicalFields)
        {
            if (!settings.PdfFieldMappings.TryGetValue(logicalField, out var mappedField) ||
                string.IsNullOrWhiteSpace(mappedField))
            {
                throw new InvalidOperationException(
                    $"The PDF field mapping for '{logicalField}' is missing.");
            }

            mappedFields.Add(mappedField.Trim());
        }
        if (mappedFields.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            mappedFields.Count)
        {
            throw new InvalidOperationException(
                "Selected device rows map to the same PDF field. Correct the Device field mappings before preparing a pickup receipt.");
        }
        if (mappedFields.Contains(
                mappedReturnDateField,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "A selected device row and ReturnDate map to the same PDF field.");
        }

        try
        {
            var fillResult = FillFields(
                sourcePath,
                destinationPath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ReturnDate"] = pickupStartedAt.ToLocalTime().ToString(
                        "MM/dd/yyyy",
                        System.Globalization.CultureInfo.InvariantCulture)
                },
                settings);
            if (fillResult.MissingPdfFields.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The mapped ReturnDate PDF field '{mappedReturnDateField}' could not be found.");
            }
            ApplyDeviceStrikeThroughsInPlace(destinationPath, mappedFields);
            _logger.Information(
                $"Created a partial-pickup child PDF with {mappedFields.Count} selected device field(s): {destinationPath}");
            return fillResult;
        }
        catch
        {
            if (File.Exists(destinationPath))
            {
                try
                {
                    File.Delete(destinationPath);
                }
                catch
                {
                    // The caller records and surfaces the original failure.
                }
            }
            throw;
        }
    }

    public string ApplyDeviceStrikeThroughs(
        string pdfPath,
        IReadOnlyCollection<string> pdfFieldNames)
    {
        ValidatePdfPath(pdfPath);
        ArgumentNullException.ThrowIfNull(pdfFieldNames);

        var requestedFields = pdfFieldNames
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (requestedFields.Length == 0)
        {
            return string.Empty;
        }

        var directory = Path.GetDirectoryName(pdfPath)
            ?? throw new InvalidOperationException("The PDF folder is invalid.");
        var baseName = Path.GetFileNameWithoutExtension(pdfPath);
        var backupPath = Path.Combine(directory, $"{baseName}-signed-original.pdf");
        if (!File.Exists(backupPath))
        {
            File.Copy(pdfPath, backupPath, overwrite: false);
        }

        ApplyDeviceStrikeThroughsInPlace(pdfPath, requestedFields);
        return backupPath;
    }

    private void ApplyDeviceStrikeThroughsInPlace(
        string pdfPath,
        IReadOnlyCollection<string> requestedFields)
    {
        var directory = Path.GetDirectoryName(pdfPath)
            ?? throw new InvalidOperationException("The PDF folder is invalid.");
        var baseName = Path.GetFileNameWithoutExtension(pdfPath);
        var tempPath = Path.Combine(directory, $".{baseName}-{Guid.NewGuid():N}.pdf");

        try
        {
            using (var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify))
            {
                if (document.AcroForm is null)
                {
                    throw new InvalidOperationException(
                        "The saved 1297 does not contain readable AcroForm fields.");
                }

                var rectanglesByPage = new Dictionary<PdfPage, List<PdfRectangle>>();
                foreach (var fieldName in requestedFields)
                {
                    var field = FindField(document.AcroForm.Fields, fieldName, string.Empty);
                    if (field is null)
                    {
                        throw new InvalidOperationException(
                            $"The PDF field '{fieldName}' could not be found for strike-through.");
                    }

                    var located = LocateFieldWidgets(document, field).ToArray();
                    if (located.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"The position of PDF field '{fieldName}' could not be determined.");
                    }

                    foreach (var item in located)
                    {
                        if (!rectanglesByPage.TryGetValue(item.Page, out var rectangles))
                        {
                            rectangles = [];
                            rectanglesByPage[item.Page] = rectangles;
                        }

                        rectangles.Add(item.Rectangle);
                    }
                }

                var pen = new XPen(XColor.FromArgb(190, 176, 32, 32), 1.8);
                foreach (var pair in rectanglesByPage)
                {
                    using var graphics = XGraphics.FromPdfPage(
                        pair.Key,
                        XGraphicsPdfPageOptions.Append);

                    var pageHeight = pair.Key.Height.Point;
                    foreach (var rectangle in pair.Value)
                    {
                        var left = Math.Min(rectangle.X1, rectangle.X2) + 2;
                        var right = Math.Max(rectangle.X1, rectangle.X2) - 2;
                        var pdfMiddleY = (rectangle.Y1 + rectangle.Y2) / 2d;
                        var drawingY = pageHeight - pdfMiddleY;
                        graphics.DrawLine(pen, left, drawingY, right, drawingY);
                    }
                }

                document.Save(tempPath);
            }

            File.Move(tempPath, pdfPath, overwrite: true);
            _logger.Information(
                $"Applied strike-through to {requestedFields.Count} device field(s) in {pdfPath}");
        }
        finally
        {
            if (File.Exists(tempPath))
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

    private static IEnumerable<(PdfPage Page, PdfRectangle Rectangle)> LocateFieldWidgets(
        PdfDocument document,
        PdfAcroField field)
    {
        var fieldReferences = EnumerateFieldTree(field)
            .Select(GetObjectIdentity)
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var page in document.Pages)
        {
            for (var index = 0; index < page.Annotations.Count; index++)
            {
                var annotation = page.Annotations[index];
                if (annotation is null)
                {
                    continue;
                }

                var annotationIdentity = GetObjectIdentity(annotation);
                var parentIdentity = annotation.Elements["/Parent"] is PdfReference parentReference
                    ? parentReference.ObjectID.ToString()
                    : string.Empty;

                if (!fieldReferences.Contains(annotationIdentity) &&
                    !fieldReferences.Contains(parentIdentity))
                {
                    continue;
                }

                yield return (page, annotation.Rectangle);
            }
        }
    }

    private static IEnumerable<PdfAcroField> EnumerateFieldTree(PdfAcroField field)
    {
        yield return field;
        if (!field.HasKids)
        {
            yield break;
        }

        for (var index = 0; index < field.Fields.Count; index++)
        {
            var child = field.Fields[index];
            if (child is null)
            {
                continue;
            }

            foreach (var descendant in EnumerateFieldTree(child))
            {
                yield return descendant;
            }
        }
    }

    private static string GetObjectIdentity(PdfObject value)
    {
        return value.Reference?.ObjectID.ToString() ?? string.Empty;
    }

    private static void SetFieldValue(PdfAcroField field, string value)
    {
        field.ReadOnly = false;

        // The cleaned production template uses /NeedAppearances=false, so every
        // filled text field must retain a concrete appearance stream. PDFsharp's
        // Text setter creates that stream, but it requires /Rect on the field object.
        // PrepareTextFieldsForAppearance supplies inherited widget geometry first.
        if (field is PdfTextField textField)
        {
            try
            {
                textField.Text = value;
                PropagateAppearanceToWidgetChildren(field);
                return;
            }
            catch (Exception exception) when
                (exception is ArgumentException or InvalidOperationException)
            {
                throw new InvalidDataException(
                    $"PDF field '{field.Name}' does not have usable widget geometry " +
                    "for generating its visible appearance.",
                    exception);
            }
        }

        field.Value = new PdfString(value);
        field.Elements.SetString("/DV", value);
    }

    private static void PrepareTextFieldsForAppearance(
        PdfAcroField.PdfAcroFieldCollection fields)
    {
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            if (field is null)
            {
                continue;
            }

            if (field is PdfTextField && field.Elements["/Rect"] is null)
            {
                var widgetRectangle = FindWidgetRectangle(field);
                if (widgetRectangle is not null)
                {
                    field.Elements["/Rect"] = widgetRectangle;
                }
            }

            if (field.HasKids)
            {
                PrepareTextFieldsForAppearance(field.Fields);
            }
        }
    }

    private static PdfItem? FindWidgetRectangle(PdfAcroField field)
    {
        if (!field.HasKids)
        {
            return null;
        }

        for (var index = 0; index < field.Fields.Count; index++)
        {
            var child = field.Fields[index];
            if (child is null)
            {
                continue;
            }

            var rectangle = child.Elements["/Rect"];
            if (rectangle is not null)
            {
                return rectangle;
            }

            var descendantRectangle = FindWidgetRectangle(child);
            if (descendantRectangle is not null)
            {
                return descendantRectangle;
            }
        }

        return null;
    }

    private static void PropagateAppearanceToWidgetChildren(PdfAcroField field)
    {
        var appearance = field.Elements["/AP"];
        if (appearance is null || !field.HasKids)
        {
            return;
        }

        for (var index = 0; index < field.Fields.Count; index++)
        {
            var child = field.Fields[index];
            if (child is null)
            {
                continue;
            }

            child.Elements["/AP"] = appearance;
            PropagateAppearanceToWidgetChildren(child);
        }
    }

    private static int CountSignatureFields(PdfAcroField.PdfAcroFieldCollection fields)
    {
        var count = 0;
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            if (field is null)
            {
                continue;
            }

            var fieldType = field.Elements["/FT"]?.ToString() ?? string.Empty;
            if (string.Equals(fieldType, "/Sig", StringComparison.Ordinal))
            {
                count++;
            }

            if (field.HasKids)
            {
                count += CountSignatureFields(field.Fields);
            }
        }

        return count;
    }

    private static PdfAcroField? FindField(
        PdfAcroField.PdfAcroFieldCollection fields,
        string targetName,
        string parentName)
    {
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            if (field is null)
            {
                continue;
            }

            var partialName = field.Name ?? string.Empty;
            var fullName = string.IsNullOrWhiteSpace(parentName)
                ? partialName
                : string.IsNullOrWhiteSpace(partialName)
                    ? parentName
                    : $"{parentName}.{partialName}";

            if (string.Equals(partialName, targetName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fullName, targetName, StringComparison.OrdinalIgnoreCase))
            {
                return field;
            }

            if (field.HasKids)
            {
                var child = FindField(field.Fields, targetName, fullName);
                if (child is not null)
                {
                    return child;
                }
            }
        }

        return null;
    }

    private static void CollectFieldNames(
        PdfAcroField.PdfAcroFieldCollection fields,
        string parentName,
        ICollection<string> names)
    {
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            if (field is null)
            {
                continue;
            }

            var partialName = field.Name ?? string.Empty;
            var fullName = string.IsNullOrWhiteSpace(parentName)
                ? partialName
                : string.IsNullOrWhiteSpace(partialName)
                    ? parentName
                    : $"{parentName}.{partialName}";

            if (!string.IsNullOrWhiteSpace(partialName))
            {
                names.Add(partialName);
            }

            if (!string.IsNullOrWhiteSpace(fullName))
            {
                names.Add(fullName);
            }

            if (field.HasKids)
            {
                CollectFieldNames(field.Fields, fullName, names);
            }
        }
    }

    private static void ValidatePdfPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A PDF path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The PDF file could not be found.", path);
        }
    }
}
