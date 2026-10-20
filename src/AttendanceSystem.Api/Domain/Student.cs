namespace AttendanceSystem.Api.Domain;

public sealed class Student
{
    public int StudentId { get; set; }

    public int SchoolId { get; set; }

    public School School { get; set; } = null!;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? Grade { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public bool Active { get; set; } = true;

    public DateTime CreatedDate { get; set; }
}
