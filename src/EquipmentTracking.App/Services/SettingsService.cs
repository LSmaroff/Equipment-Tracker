using System.IO;
using System.Text.Json;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly FileLogger _logger;

    public SettingsService(AppPaths paths, FileLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public AppSettings Current { get; private set; } = new();

    public async Task LoadAsync()
    {
        _paths.EnsureDirectories();

        if (!File.Exists(_paths.SettingsPath))
        {
            var defaultPath = Path.Combine(AppContext.BaseDirectory, "appsettings.default.json");
            if (File.Exists(defaultPath))
            {
                File.Copy(defaultPath, _paths.SettingsPath, overwrite: false);
            }
            else
            {
                await SaveAsync(new AppSettings());
            }
        }

        try
        {
            const long maximumSettingsBytes = 1024 * 1024;
            if (new FileInfo(_paths.SettingsPath).Length > maximumSettingsBytes)
            {
                throw new InvalidDataException("The settings file is larger than the supported 1 MB limit.");
            }

            await using var stream = File.OpenRead(_paths.SettingsPath);
            Current = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions)
                ?? new AppSettings();

            Current.Theme = ThemeService.Normalize(Current.Theme);
            Current.MaximumDevicesPerForm = Math.Clamp(Current.MaximumDevicesPerForm, 1, 10);
            Current.LogRetentionDays = Math.Clamp(Current.LogRetentionDays, 1, 3650);
            Current.TemporaryFileRetentionDays = Math.Clamp(Current.TemporaryFileRetentionDays, 1, 3650);
            Current.CompletedWorkflowRetentionDays = Math.Clamp(Current.CompletedWorkflowRetentionDays, 1, 3650);
            Current.BackupRetentionCount = Math.Clamp(Current.BackupRetentionCount, 1, 100);
            Current.ScheduledBackupFolder = NormalizeConfiguredPath(
                Current.ScheduledBackupFolder,
                new AppSettings().ScheduledBackupFolder);

            Current.Organizations ??= [];
            Current.Organizations = Current.Organizations
                .Select(value => value?.Trim() ?? string.Empty)
                .Where(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 120)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (Current.Organizations.Count == 0)
            {
                Current.Organizations.Add("58 SOW");
            }

            Current.PdfFieldMappings ??= AppSettings.CreateDefaultPdfFieldMappings();
            Current.PdfFieldMappings = new Dictionary<string, string>(
                Current.PdfFieldMappings,
                StringComparer.OrdinalIgnoreCase);

            var defaultMappings = AppSettings.CreateDefaultPdfFieldMappings();
            if (!Current.PdfFieldMappings.ContainsKey("Device1"))
            {
                Current.PdfFieldMappings = defaultMappings;
            }
            else
            {
                foreach (var mapping in defaultMappings)
                {
                    Current.PdfFieldMappings.TryAdd(mapping.Key, mapping.Value);
                }
            }

            RepairLegacySignatureMappings(Current);

            // The operational 1297 template uses this exact field name. Repair any
            // stale or misspelled value from an older persisted settings file.
            Current.PdfFieldMappings["PickupSignature"] = AppSettings.PickupSignatureFieldName;
        }
        catch (Exception ex)
        {
            _logger.Error("Settings could not be loaded. Default settings will be used.", ex);
            Current = new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _paths.EnsureDirectories();

        settings.Theme = ThemeService.Normalize(settings.Theme);
        settings.MaximumDevicesPerForm = Math.Clamp(settings.MaximumDevicesPerForm, 1, 10);
        settings.LogRetentionDays = Math.Clamp(settings.LogRetentionDays, 1, 3650);
        settings.TemporaryFileRetentionDays = Math.Clamp(settings.TemporaryFileRetentionDays, 1, 3650);
        settings.CompletedWorkflowRetentionDays = Math.Clamp(settings.CompletedWorkflowRetentionDays, 1, 3650);
        settings.BackupRetentionCount = Math.Clamp(settings.BackupRetentionCount, 1, 100);
        settings.ScheduledBackupFolder = NormalizeConfiguredPath(
            settings.ScheduledBackupFolder,
            new AppSettings().ScheduledBackupFolder);
        settings.Organizations = settings.Organizations
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 120)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (settings.Organizations.Count == 0)
        {
            throw new InvalidOperationException("At least one organization is required.");
        }

        settings.PdfFieldMappings ??= AppSettings.CreateDefaultPdfFieldMappings();
        settings.PdfFieldMappings = new Dictionary<string, string>(
            settings.PdfFieldMappings,
            StringComparer.OrdinalIgnoreCase);
        settings.PdfFieldMappings["PickupSignature"] = AppSettings.PickupSignatureFieldName;

        var tempPath = _paths.SettingsPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
        }

        File.Move(tempPath, _paths.SettingsPath, overwrite: true);
        Current = settings.Clone();
        _logger.Information("Application settings were saved.");
    }

    public string ResolvePath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return string.Empty;
        }

        var expanded = Environment.ExpandEnvironmentVariables(configuredPath.Trim());

        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, expanded));
    }

    private static string NormalizeConfiguredPath(string? value, string fallback)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return fallback;
        }
        if (normalized.Length > 1024)
        {
            throw new InvalidDataException("A configured path exceeds the supported 1,024-character limit.");
        }
        return normalized;
    }

    private static void RepairLegacySignatureMappings(AppSettings settings)
    {
        var defaultTemplatePath = new AppSettings().TemplatePdfPath;
        if (!string.Equals(
                settings.TemplatePdfPath?.Trim(),
                defaultTemplatePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var mappings = settings.PdfFieldMappings;
        if (mappings.TryGetValue("TechnicianSignature", out var technicianSignature) &&
            mappings.TryGetValue("CustomerSignature", out var customerSignature) &&
            string.Equals(
                technicianSignature?.Trim(),
                "ISSUED TO SIGNATURE",
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                customerSignature?.Trim(),
                "ISSUED BY SIGNATURE",
                StringComparison.OrdinalIgnoreCase))
        {
            mappings["TechnicianSignature"] = "ISSUED BY SIGNATURE";
            mappings["CustomerSignature"] = "ISSUED TO SIGNATURE";
        }
    }
}
