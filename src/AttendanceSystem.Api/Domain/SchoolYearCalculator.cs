namespace AttendanceSystem.Api.Domain;

public static class SchoolYearCalculator
{
    public static string GetSchoolYear(DateOnly date)
    {
        var startYear = date.Month >= 9 ? date.Year : date.Year - 1;
        return $"{startYear}-{startYear + 1}";
    }

    public static (DateOnly Start, DateOnly End) GetRange(string schoolYear)
    {
        var startYear = int.Parse(schoolYear.AsSpan(0, 4), System.Globalization.CultureInfo.InvariantCulture);
        return (new DateOnly(startYear, 9, 1), new DateOnly(startYear + 1, 8, 31));
    }

    public static (DateOnly Start, DateOnly End) GetRange(DateOnly date) => GetRange(GetSchoolYear(date));
}
