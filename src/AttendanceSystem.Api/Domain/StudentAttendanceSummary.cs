namespace AttendanceSystem.Api.Domain;

public sealed class StudentAttendanceSummary
{
    public int SummaryId { get; set; }

    public int StudentId { get; set; }

    public int SchoolId { get; set; }

    public string SchoolYear { get; set; } = string.Empty;

    public int TotalAbsences { get; set; }

    public DateTime LastUpdated { get; set; }
}
