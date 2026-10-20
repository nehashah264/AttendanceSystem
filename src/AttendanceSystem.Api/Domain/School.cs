namespace AttendanceSystem.Api.Domain;

public sealed class School
{
    public int SchoolId { get; set; }

    public string SchoolName { get; set; } = string.Empty;

    public bool Active { get; set; } = true;

    public int? AbsenceAlertThreshold { get; set; }
}
