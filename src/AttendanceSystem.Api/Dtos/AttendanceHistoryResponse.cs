namespace AttendanceSystem.Api.Dtos;

public sealed class AttendanceHistoryResponse
{
    public int StudentId { get; set; }

    public string SchoolYear { get; set; } = string.Empty;

    public IReadOnlyList<AttendanceHistoryItem> Records { get; set; } = Array.Empty<AttendanceHistoryItem>();
}

public sealed class AttendanceHistoryItem
{
    public DateOnly AttendDate { get; set; }

    public string AttendCode { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsAbsent { get; set; }

    public bool IsExcused { get; set; }

    public int? MinutesLate { get; set; }

    public string? Notes { get; set; }

    public string? TermName { get; set; }
}
