using System.Globalization;
using System.Text.RegularExpressions;
using AttendanceSystem.Api.Dtos;
using FluentValidation;

namespace AttendanceSystem.Api.Validation;

public sealed class StudentYearQueryValidator : AbstractValidator<StudentYearQuery>
{
    private static readonly Regex YearPattern = new(@"^\d{4}-\d{4}$", RegexOptions.CultureInvariant);

    public StudentYearQueryValidator()
    {
        RuleFor(query => query.StudentId).GreaterThan(0);
        RuleFor(query => query.SchoolYear)
            .Must(BeSchoolYear)
            .When(query => !string.IsNullOrWhiteSpace(query.SchoolYear))
            .WithMessage("SchoolYear must be in the form YYYY-YYYY.");
    }

    private static bool BeSchoolYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !YearPattern.IsMatch(value))
        {
            return false;
        }

        var start = int.Parse(value[..4], CultureInfo.InvariantCulture);
        var end = int.Parse(value[5..], CultureInfo.InvariantCulture);
        return end == start + 1;
    }
}
