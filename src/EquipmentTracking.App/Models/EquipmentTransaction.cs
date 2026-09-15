namespace EquipmentTracking.App.Models;

public sealed class EquipmentTransaction
{
    public string Id { get; set; } = string.Empty;
    public CustomerIdentity Customer { get; set; } = new();
    public string PhoneNumber { get; set; } = string.Empty;
    public string Technician { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string TicketNumber { get; set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string PdfPath { get; set; } = string.Empty;
    public string PdfSignerName { get; set; } = string.Empty;
    public string CertificateSubject { get; set; } = string.Empty;
    public string CertificateThumbprint { get; set; } = string.Empty;
    public DateTimeOffset? SignatureTime { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset? CloseoutPreparedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string CloseoutTechnician { get; set; } = string.Empty;
    public List<DeviceRecord> Devices { get; set; } = [];
}
