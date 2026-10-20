namespace AttendanceSystem.Api.Dtos;

public sealed class StudentYearQuery
{
    public int StudentId { get; set; }

    public string? SchoolYear { get; set; }
}
