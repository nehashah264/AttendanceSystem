namespace AttendanceSystem.Api.Domain;

public sealed class AttendanceCode
{
    public int CodeId { get; set; }

    public string CodeValue { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsAbsent { get; set; }

    public bool IsExcused { get; set; }

    public bool IsActive { get; set; } = true;
}
