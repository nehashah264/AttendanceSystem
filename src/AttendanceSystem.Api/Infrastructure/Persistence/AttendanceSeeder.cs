using AttendanceSystem.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Api.Infrastructure.Persistence;

public static class AttendanceSeeder
{
    private static readonly DateTime SeededAt = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    public static async Task SeedAsync(AttendanceDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Schools.AnyAsync(cancellationToken))
        {
            return;
        }

        var lincoln = new School
        {
            SchoolName = "Lincoln Elementary",
            Active = true,
            AbsenceAlertThreshold = 10
        };
        var riverside = new School
        {
            SchoolName = "Riverside High",
            Active = true,
            AbsenceAlertThreshold = 5
        };
        var closed = new School
        {
            SchoolName = "Closed Academy",
            Active = false,
            AbsenceAlertThreshold = 10
        };
        db.Schools.AddRange(lincoln, riverside, closed);
        await db.SaveChangesAsync(cancellationToken);

        db.AttendanceCodes.AddRange(
            new AttendanceCode { CodeValue = "P", Description = "Present", IsAbsent = false, IsExcused = false, IsActive = true },
            new AttendanceCode { CodeValue = "A", Description = "Absent", IsAbsent = true, IsExcused = false, IsActive = true },
            new AttendanceCode { CodeValue = "E", Description = "Excused absence", IsAbsent = true, IsExcused = true, IsActive = true },
            new AttendanceCode { CodeValue = "T", Description = "Tardy", IsAbsent = false, IsExcused = false, IsActive = true });
        await db.SaveChangesAsync(cancellationToken);

        db.SchoolTerms.AddRange(
            Term(lincoln.SchoolId, "Fall 2025", new DateOnly(2025, 9, 1), new DateOnly(2026, 1, 31)),
            Term(lincoln.SchoolId, "Spring 2026", new DateOnly(2026, 2, 1), new DateOnly(2026, 8, 31)),
            Term(lincoln.SchoolId, "Fall 2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31)),
            Term(lincoln.SchoolId, "Spring 2027", new DateOnly(2027, 2, 1), new DateOnly(2027, 8, 31)),
            Term(riverside.SchoolId, "Fall 2025", new DateOnly(2025, 9, 1), new DateOnly(2026, 1, 31)),
            Term(riverside.SchoolId, "Spring 2026", new DateOnly(2026, 2, 1), new DateOnly(2026, 8, 31)),
            Term(riverside.SchoolId, "Fall 2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31)),
            Term(riverside.SchoolId, "Spring 2027", new DateOnly(2027, 2, 1), new DateOnly(2027, 8, 31)));
        await db.SaveChangesAsync(cancellationToken);

        var ada = Student(lincoln.SchoolId, "Ada", "Lovelace", "5", true, new DateOnly(2015, 1, 1));
        var alan = Student(lincoln.SchoolId, "Alan", "Turing", "5", true, new DateOnly(2015, 6, 23));
        var grace = Student(lincoln.SchoolId, "Grace", "Hopper", "6", true, new DateOnly(2014, 12, 9));
        var bob = Student(lincoln.SchoolId, "Bob", "Inactive", "6", false, new DateOnly(2014, 3, 3));
        var katherine = Student(riverside.SchoolId, "Katherine", "Johnson", "9", true, new DateOnly(2011, 8, 26));
        var marie = Student(riverside.SchoolId, "Marie", "Curie", "10", true, new DateOnly(2010, 11, 7));
        var helen = Student(closed.SchoolId, "Helen", "Keller", "4", true, new DateOnly(2016, 6, 27));
        db.Students.AddRange(ada, alan, grace, bob, katherine, marie, helen);
        await db.SaveChangesAsync(cancellationToken);

        var lincolnFall2026 = await db.SchoolTerms
            .Where(term => term.SchoolId == lincoln.SchoolId && term.TermName == "Fall 2026")
            .Select(term => term.TermId)
            .SingleAsync(cancellationToken);
        var riversideFall2026 = await db.SchoolTerms
            .Where(term => term.SchoolId == riverside.SchoolId && term.TermName == "Fall 2026")
            .Select(term => term.TermId)
            .SingleAsync(cancellationToken);
        var riversideSpring2026 = await db.SchoolTerms
            .Where(term => term.SchoolId == riverside.SchoolId && term.TermName == "Spring 2026")
            .Select(term => term.TermId)
            .SingleAsync(cancellationToken);

        for (var day = 1; day <= 10; day++)
        {
            db.StudentAttendances.Add(Attendance(grace.StudentId, lincoln.SchoolId, new DateOnly(2026, 9, day), lincolnFall2026, "A", true, false, 0));
        }

        db.StudentAttendances.Add(Attendance(alan.StudentId, lincoln.SchoolId, new DateOnly(2026, 9, 20), lincolnFall2026, "P", false, false, 0));
        db.StudentAttendances.Add(Attendance(katherine.StudentId, riverside.SchoolId, new DateOnly(2026, 9, 15), riversideFall2026, "A", true, false, 0));
        db.StudentAttendances.Add(Attendance(katherine.StudentId, riverside.SchoolId, new DateOnly(2026, 9, 16), riversideFall2026, "A", true, false, 0));
        db.StudentAttendances.Add(Attendance(marie.StudentId, riverside.SchoolId, new DateOnly(2026, 5, 12), riversideSpring2026, "A", true, false, 0));
        db.StudentAttendances.Add(Attendance(marie.StudentId, riverside.SchoolId, new DateOnly(2026, 5, 13), riversideSpring2026, "P", false, false, 0));
        await db.SaveChangesAsync(cancellationToken);

        db.StudentAttendanceSummaries.AddRange(
            Summary(grace.StudentId, lincoln.SchoolId, "2026-2027", 10),
            Summary(katherine.StudentId, riverside.SchoolId, "2026-2027", 2),
            Summary(marie.StudentId, riverside.SchoolId, "2025-2026", 1),
            Summary(alan.StudentId, lincoln.SchoolId, "2026-2027", 0));
        db.StudentAlerts.Add(new StudentAlert
        {
            StudentId = grace.StudentId,
            SchoolId = lincoln.SchoolId,
            AlertType = AlertTypes.ChronicAbsence,
            SchoolYear = "2026-2027",
            AlertDate = SeededAt,
            AlertMessage = "Student has reached 10 absences this year"
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static SchoolTerm Term(int schoolId, string name, DateOnly start, DateOnly end) => new()
    {
        SchoolId = schoolId,
        TermName = name,
        StartDate = start,
        EndDate = end
    };

    private static Student Student(int schoolId, string firstName, string lastName, string grade, bool active, DateOnly dateOfBirth) => new()
    {
        SchoolId = schoolId,
        FirstName = firstName,
        LastName = lastName,
        Grade = grade,
        Active = active,
        DateOfBirth = dateOfBirth,
        CreatedDate = SeededAt
    };

    private static StudentAttendance Attendance(
        int studentId,
        int schoolId,
        DateOnly date,
        int termId,
        string code,
        bool isAbsent,
        bool isExcused,
        int minutesLate) => new()
    {
        StudentId = studentId,
        SchoolId = schoolId,
        AttendDate = date,
        TermId = termId,
        AttendCode = code,
        IsAbsent = isAbsent,
        IsExcused = isExcused,
        MinutesLate = minutesLate,
        CreatedDate = SeededAt,
        CreatedBy = "seed"
    };

    private static StudentAttendanceSummary Summary(int studentId, int schoolId, string schoolYear, int total) => new()
    {
        StudentId = studentId,
        SchoolId = schoolId,
        SchoolYear = schoolYear,
        TotalAbsences = total,
        LastUpdated = SeededAt
    };
}
