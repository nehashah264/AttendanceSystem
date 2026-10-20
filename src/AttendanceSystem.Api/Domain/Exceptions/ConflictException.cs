namespace AttendanceSystem.Api.Domain.Exceptions;

public sealed class ConflictException : Exception
{
    public ConflictException(string message, IReadOnlyList<int> studentIds)
        : base(message)
    {
        StudentIds = studentIds;
    }

    public IReadOnlyList<int> StudentIds { get; }
}
