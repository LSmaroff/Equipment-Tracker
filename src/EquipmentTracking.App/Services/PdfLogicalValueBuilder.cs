using System.Globalization;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public static class PdfLogicalValueBuilder
{
    public static Dictionary<string, string> Build(
        string phoneNumber,
        string customerNameGrade,
        string organization,
        string ticketNumber,
        IReadOnlyList<DeviceRecord> devices,
        DateTimeOffset? issueDate = null,
        DateTimeOffset? returnDate = null)
    {
        ArgumentNullException.ThrowIfNull(devices);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PhoneNumber"] = phoneNumber?.Trim() ?? string.Empty,
            ["TechnicianNameGrade"] = customerNameGrade?.Trim() ?? string.Empty,
            ["Organization"] = organization?.Trim() ?? string.Empty,
            ["TicketNumber"] = ticketNumber?.Trim() ?? string.Empty,
            ["IssueDate"] = FormatDate(issueDate ?? DateTimeOffset.Now),
            ["ReturnDate"] = returnDate is null ? string.Empty : FormatDate(returnDate.Value),
            ["Quantity"] = devices.Count.ToString(CultureInfo.InvariantCulture)
        };

        for (var index = 0; index < devices.Count; index++)
        {
            values[$"Device{index + 1}"] = BuildDeviceBlock(devices[index]);
        }

        return values;
    }

    public static string BuildDeviceBlock(DeviceRecord device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(device.Model))
        {
            lines.Add($"Model Name: {device.Model.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(device.PartNumber))
        {
            lines.Add($"Part Number: {device.PartNumber.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(device.SerialNumber))
        {
            lines.Add($"Serial Number: {device.SerialNumber.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(device.AssetTag))
        {
            lines.Add($"Asset Tag: {device.AssetTag.Trim()}");
        }

        return string.Join("\r\n", lines);
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToLocalTime().ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
}
