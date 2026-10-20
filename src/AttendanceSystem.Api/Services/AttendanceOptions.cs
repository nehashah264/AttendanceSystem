namespace AttendanceSystem.Api.Services;

public sealed class AttendanceOptions
{
    public const string SectionName = "Attendance";

    public int ChronicAbsenceDays { get; set; } = 10;
}
