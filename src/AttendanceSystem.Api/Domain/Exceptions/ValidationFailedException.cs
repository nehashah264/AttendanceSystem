namespace AttendanceSystem.Api.Domain.Exceptions;

public sealed class ValidationFailedException : Exception
{
    public ValidationFailedException(IDictionary<string, string[]> errors)
        : base("Validation failed.")
    {
        Errors = errors;
    }

    public IDictionary<string, string[]> Errors { get; }
}
