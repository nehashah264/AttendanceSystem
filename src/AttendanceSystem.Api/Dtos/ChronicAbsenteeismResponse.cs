namespace AttendanceSystem.Api.Dtos;

public sealed class ChronicAbsenteeismResponse
{
    public int StudentId { get; set; }

    public string SchoolYear { get; set; } = string.Empty;

    public int TotalAbsences { get; set; }

    public int AbsenceAlertThreshold { get; set; }

    public bool IsChronicallyAbsent { get; set; }
}
