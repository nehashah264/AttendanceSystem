namespace AttendanceSystem.Api.Dtos;

public sealed class SaveDailyAttendanceResponse
{
    public int SchoolId { get; set; }

    public DateOnly AttendDate { get; set; }

    public string SchoolYear { get; set; } = string.Empty;

    public int? TermId { get; set; }

    public int RecordCount { get; set; }

    public IReadOnlyList<SavedAttendanceItem> Students { get; set; } = Array.Empty<SavedAttendanceItem>();
}

public sealed class SavedAttendanceItem
{
    public int StudentId { get; set; }

    public string AttendCode { get; set; } = string.Empty;

    public bool IsAbsent { get; set; }

    public bool IsExcused { get; set; }

    public int MinutesLate { get; set; }

    public string? Notes { get; set; }

    public string Operation { get; set; } = "Inserted";
}
