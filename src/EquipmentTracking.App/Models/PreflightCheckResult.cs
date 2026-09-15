namespace EquipmentTracking.App.Models;

public enum PreflightStatus
{
    Passed,
    Warning,
    Failed
}

public sealed class PreflightCheckResult
{
    public string Name { get; init; } = string.Empty;
    public PreflightStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool IsBlocking { get; init; }

    public string StatusText => Status switch
    {
        PreflightStatus.Passed => "Ready",
        PreflightStatus.Warning => "Warning",
        _ => "Failed"
    };
}
