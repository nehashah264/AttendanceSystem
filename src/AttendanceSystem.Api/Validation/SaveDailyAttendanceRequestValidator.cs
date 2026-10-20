using AttendanceSystem.Api.Dtos;
using FluentValidation;

namespace AttendanceSystem.Api.Validation;

public sealed class SaveDailyAttendanceRequestValidator : AbstractValidator<SaveDailyAttendanceRequest>
{
    public SaveDailyAttendanceRequestValidator()
    {
        RuleFor(request => request.SchoolId).GreaterThan(0);
        RuleFor(request => request.AttendDate)
            .Must(date => date >= new DateOnly(2000, 1, 1))
            .WithMessage("AttendDate is required.");
        RuleFor(request => request.SubmittedBy).MaximumLength(100);
        RuleFor(request => request.Students).NotEmpty();
        RuleForEach(request => request.Students).SetValidator(new AttendanceEntryRequestValidator());
    }
}

public sealed class AttendanceEntryRequestValidator : AbstractValidator<AttendanceEntryRequest>
{
    public AttendanceEntryRequestValidator()
    {
        RuleFor(entry => entry.StudentId).GreaterThan(0);
        RuleFor(entry => entry.AttendCode).MaximumLength(5);
        RuleFor(entry => entry.MinutesLate).GreaterThanOrEqualTo(0);
        RuleFor(entry => entry.Notes).MaximumLength(500);
    }
}
