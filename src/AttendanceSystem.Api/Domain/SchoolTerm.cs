namespace AttendanceSystem.Api.Domain;

public sealed class SchoolTerm
{
    public int TermId { get; set; }

    public int SchoolId { get; set; }

    public string TermName { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }
}
