namespace AttendanceSystem.Api.Dtos;

public sealed class SaveDailyAttendanceRequest
{
    public int SchoolId { get; set; }

    public DateOnly AttendDate { get; set; }

    public string? SubmittedBy { get; set; }

    public List<AttendanceEntryRequest> Students { get; set; } = new();
}

public sealed class AttendanceEntryRequest
{
    public int StudentId { get; set; }

    public string AttendCode { get; set; } = string.Empty;

    public int MinutesLate { get; set; }

    public string? Notes { get; set; }
}
