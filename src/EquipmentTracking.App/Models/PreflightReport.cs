namespace EquipmentTracking.App.Models;

public sealed class PreflightReport
{
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.Now;
    public List<PreflightCheckResult> Checks { get; init; } = [];
    public bool HasBlockingFailures => Checks.Any(item => item.Status == PreflightStatus.Failed && item.IsBlocking);
    public bool HasWarningsOrFailures => Checks.Any(item => item.Status != PreflightStatus.Passed);
    public int PassedCount => Checks.Count(item => item.Status == PreflightStatus.Passed);
    public int WarningCount => Checks.Count(item => item.Status == PreflightStatus.Warning);
    public int FailedCount => Checks.Count(item => item.Status == PreflightStatus.Failed);
}
