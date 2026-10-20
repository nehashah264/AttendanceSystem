namespace AttendanceSystem.Api.Domain;

public static class ChronicAbsenteeismCalculator
{
    public static int ResolveThreshold(int? schoolThreshold, int configuredChronicDays) =>
        schoolThreshold ?? configuredChronicDays;

    public static bool IsChronicallyAbsent(int totalAbsences, int threshold) =>
        totalAbsences >= threshold;
}
