namespace AttendanceSystem.Api.Domain;

public sealed class StudentAlert
{
    public int AlertId { get; set; }

    public int StudentId { get; set; }

    public int SchoolId { get; set; }

    public string AlertType { get; set; } = string.Empty;

    public string SchoolYear { get; set; } = string.Empty;

    public DateTime AlertDate { get; set; }

    public string? AlertMessage { get; set; }

    public DateTime? ResolvedDate { get; set; }

    public string? ResolvedBy { get; set; }
}
