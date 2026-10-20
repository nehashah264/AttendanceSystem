namespace AttendanceSystem.Api.Domain;

public sealed class AttendanceSubmissionLog
{
    public int LogId { get; set; }

    public int SchoolId { get; set; }

    public DateTime SubmittedDate { get; set; }

    public DateOnly AttendDate { get; set; }

    public int RecordCount { get; set; }

    public string? SubmittedBy { get; set; }
}
