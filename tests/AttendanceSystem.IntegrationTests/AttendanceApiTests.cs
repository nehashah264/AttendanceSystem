using System.Net;
using System.Net.Http.Json;
using AttendanceSystem.Api.Domain;
using AttendanceSystem.Api.Dtos;
using AttendanceSystem.Api.Infrastructure.Persistence;
using AttendanceSystem.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AttendanceSystem.IntegrationTests;

public sealed class AttendanceApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"attendance-{Guid.NewGuid():N}.db");
    private readonly bool _throwBeforeCommit;

    public AttendanceApiFactory(bool throwBeforeCommit = false)
    {
        _throwBeforeCommit = throwBeforeCommit;
    }

    public string ConnectionString => $"Data Source={_databasePath};Pooling=False";

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Attendance", ConnectionString);
        builder.ConfigureTestServices(services =>
        {
            var registrations = services.Where(descriptor =>
                descriptor.ServiceType == typeof(DbContextOptions<AttendanceDbContext>)
                || descriptor.ServiceType == typeof(AttendanceDbContext)).ToList();
            foreach (var registration in registrations)
            {
                services.Remove(registration);
            }

            services.AddDbContext<AttendanceDbContext>(options => options.UseSqlite(ConnectionString));

            if (_throwBeforeCommit)
            {
                var faults = services.Where(descriptor => descriptor.ServiceType == typeof(ISaveFaultInjector)).ToList();
                foreach (var fault in faults)
                {
                    services.Remove(fault);
                }

                services.AddScoped<ISaveFaultInjector, ThrowingSaveFaultInjector>();
            }
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}

public sealed class ThrowingSaveFaultInjector : ISaveFaultInjector
{
    public void ThrowIfRequested() =>
        throw new InvalidOperationException("Injected fault before commit.");
}

public sealed class AttendanceApiTests
{
    [Fact]
    public async Task Save_Returns201_AndPersistsAttendanceSummaryAndLog()
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/attendance/daily", Batch(1, new DateOnly(2026, 10, 22), "clerk",
            Entry(1, "P", 0, null),
            Entry(2, "A", 5, "bus")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SaveDailyAttendanceResponse>();
        Assert.NotNull(body);
        Assert.Equal("2026-2027", body.SchoolYear);
        Assert.Equal(2, body.RecordCount);
        Assert.Contains(body.Students, item => item.StudentId == 1 && !item.IsAbsent);
        Assert.Contains(body.Students, item => item.StudentId == 2 && item.IsAbsent && item.MinutesLate == 5 && item.Notes == "bus");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var termId = await db.SchoolTerms.Where(term => term.SchoolId == 1 && term.TermName == "Fall 2026").Select(term => term.TermId).SingleAsync();
        Assert.Equal(termId, body.TermId);
        Assert.Equal(2, await db.StudentAttendances.CountAsync(row => row.AttendDate == new DateOnly(2026, 10, 22)));
        Assert.Equal(0, await db.StudentAttendanceSummaries.Where(row => row.StudentId == 1 && row.SchoolYear == "2026-2027").Select(row => row.TotalAbsences).SingleAsync());
        Assert.Equal(1, await db.StudentAttendanceSummaries.Where(row => row.StudentId == 2 && row.SchoolYear == "2026-2027").Select(row => row.TotalAbsences).SingleAsync());
        var log = await db.AttendanceSubmissionLogs.SingleAsync();
        Assert.Equal(2, log.RecordCount);
        Assert.Equal("SYSTEM_USER", log.SubmittedBy);
    }

    [Fact]
    public async Task Save_Present_DoesNotIncreaseAbsences()
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/attendance/daily", Batch(1, new DateOnly(2026, 10, 21), null, Entry(1, "P", 0, null)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        Assert.Equal(0, await db.StudentAttendanceSummaries.Where(row => row.StudentId == 1).Select(row => row.TotalAbsences).SingleAsync());
    }

    [Fact]
    public async Task Save_ExcusedAbsence_CountsTowardTheTotal()
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/attendance/daily", Batch(1, new DateOnly(2026, 10, 21), null, Entry(1, "E", 0, "appointment")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SaveDailyAttendanceResponse>();
        Assert.True(body!.Students.Single().IsAbsent);
        Assert.True(body.Students.Single().IsExcused);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        Assert.Equal(1, await db.StudentAttendanceSummaries.Where(row => row.StudentId == 1).Select(row => row.TotalAbsences).SingleAsync());
    }

    [Fact]
    public async Task Save_Tardy_DoesNotCountAsAbsent()
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/attendance/daily", Batch(1, new DateOnly(2026, 10, 21), null, Entry(2, "T", 15, null)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var row = await db.StudentAttendances.SingleAsync(item => item.StudentId == 2 && item.AttendDate == new DateOnly(2026, 10, 21));
        Assert.Equal(15, row.MinutesLate);
        Assert.False(row.IsAbsent);
        Assert.Equal(0, await db.StudentAttendanceSummaries.Where(item => item.StudentId == 2 && item.SchoolYear == "2026-2027").Select(item => item.TotalAbsences).SingleAsync());
    }

    [Fact]
    public async Task Save_WhenDayExists_UpdatesThatStudent_AndInsertsTheOthers()
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var date = new DateOnly(2026, 10, 23);
        var first = await client.PostAsJsonAsync("/api/attendance/daily", Batch(1, date, "clerk", Entry(1, "P", 0, null)));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/attendance/daily", Batch(1, date, "clerk", Entry(1, "A", 4, "corrected"), Entry(2, "P", 0, null)));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<SaveDailyAttendanceResponse>();
        Assert.Equal("Updated", body!.Students.Single(item => item.StudentId == 1).Operation);
        Assert.Equal("Inserted", body.Students.Single(item => item.StudentId == 2).Operation);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        Assert.Equal(2, await db.StudentAttendances.CountAsync(row => row.AttendDate == date));
        var updated = await db.StudentAttendances.SingleAsync(row => row.StudentId == 1 && row.AttendDate == date);
        Assert.Equal("A", updated.AttendCode);
        Assert.True(updated.IsAbsent);
        Assert.Equal(4, updated.MinutesLate);
        Assert.Equal("corrected", updated.Notes);
        Assert.Equal("SYSTEM_USER", updated.ModifiedBy);
        Assert.Equal("SYSTEM_USER", updated.CreatedBy);
        Assert.NotNull(updated.ModifiedDate);
        Assert.Equal(1, await db.StudentAttendanceSummaries.Where(row => row.StudentId == 1 && row.SchoolYear == "2026-2027").Select(row => row.TotalAbsences).SingleAsync());
        Assert.Equal(0, await db.StudentAttendanceSummaries.Where(row => row.StudentId == 2 && row.SchoolYear == "2026-2027").Select(row => row.TotalAbsences).SingleAsync());
        Assert.Equal(2, await db.AttendanceSubmissionLogs.CountAsync());
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("negative")]
    [InlineData("unknown")]
    public async Task Save_WhenInvalid_Returns400_AndWritesNothing(string kind)
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var request = kind switch
        {
            "empty" => new SaveDailyAttendanceRequest { SchoolId = 1, AttendDate = new DateOnly(2026, 10, 24), Students = [] },
            "negative" => Batch(1, new DateOnly(2026, 10, 24), null, Entry(1, "P", -1, null)),
            _ => Batch(1, new DateOnly(2026, 10, 24), null, Entry(1, "Z", 0, null))
        };

        var response = await client.PostAsJsonAsync("/api/attendance/daily", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"errors\"", payload, StringComparison.Ordinal);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        Assert.False(await db.StudentAttendances.AnyAsync(row => row.AttendDate == new DateOnly(2026, 10, 24)));
        Assert.False(await db.AttendanceSubmissionLogs.AnyAsync());
    }

    [Fact]
    public async Task Save_WhenNotesTooLong_Returns400()
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/attendance/daily", Batch(1, new DateOnly(2026, 10, 24), null, Entry(1, "P", 0, new string('n', 501))));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Save_WhenCodeBlank_StoresPresent()
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var date = new DateOnly(2026, 10, 26);
        var response = await client.PostAsJsonAsync("/api/attendance/daily", Batch(1, date, "clerk", Entry(1, "", 0, null)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var row = await db.StudentAttendances.SingleAsync(item => item.StudentId == 1 && item.AttendDate == date);
        Assert.Equal("P", row.AttendCode);
        Assert.False(row.IsAbsent);
        Assert.Equal("SYSTEM_USER", row.CreatedBy);
    }

    [Fact]
    public async Task Save_WhenStudentRepeated_KeepsTheLastCode()
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var date = new DateOnly(2026, 10, 27);
        var response = await client.PostAsJsonAsync("/api/attendance/daily", Batch(1, date, "clerk", Entry(1, "P", 0, null), Entry(1, "A", 3, "second")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SaveDailyAttendanceResponse>();
        Assert.Single(body!.Students);
        Assert.Equal("A", body.Students.Single().AttendCode);
        Assert.Equal("Inserted", body.Students.Single().Operation);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        Assert.Equal(1, await db.StudentAttendances.CountAsync(row => row.AttendDate == date));
        var row = await db.StudentAttendances.SingleAsync(item => item.AttendDate == date);
        Assert.Equal("A", row.AttendCode);
        Assert.Equal(3, row.MinutesLate);
        Assert.Equal(2, await db.AttendanceSubmissionLogs.Where(log => log.AttendDate == date).Select(log => log.RecordCount).SingleAsync());
    }

    [Fact]
    public async Task Save_WhenCodeInactive_StillSavesItsFlags()
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        await using (var setup = factory.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            var tardy = await db.AttendanceCodes.SingleAsync(code => code.CodeValue == "T");
            tardy.IsActive = false;
            await db.SaveChangesAsync();
        }

        var date = new DateOnly(2026, 10, 28);
        var response = await client.PostAsJsonAsync("/api/attendance/daily", Batch(1, date, null, Entry(2, "T", 8, null)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var row = await database.StudentAttendances.SingleAsync(item => item.StudentId == 2 && item.AttendDate == date);
        Assert.Equal("T", row.AttendCode);
        Assert.False(row.IsAbsent);
        Assert.Equal(8, row.MinutesLate);
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(3, 7)]
    [InlineData(1, 5)]
    public async Task Save_WhenInactiveOrOtherSchool_StillSaves(int schoolId, int studentId)
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var date = new DateOnly(2026, 11, studentId);
        var response = await client.PostAsJsonAsync("/api/attendance/daily", Batch(schoolId, date, null, Entry(studentId, "P", 0, null)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var row = await db.StudentAttendances.SingleAsync(item => item.StudentId == studentId && item.AttendDate == date);
        Assert.Equal(schoolId, row.SchoolId);
        Assert.Equal("P", row.AttendCode);
    }

    [Fact]
    public async Task Save_WhenUpdating_LeavesSchoolTermAndSummarySchool()
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var date = new DateOnly(2026, 10, 29);
        var first = await client.PostAsJsonAsync("/api/attendance/daily", Batch(2, date, null, Entry(5, "P", 0, null)));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/attendance/daily", Batch(1, date, null, Entry(5, "A", 0, "moved")));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var termId = await db.SchoolTerms.Where(term => term.SchoolId == 2 && term.TermName == "Fall 2026").Select(term => term.TermId).SingleAsync();
        var row = await db.StudentAttendances.SingleAsync(item => item.StudentId == 5 && item.AttendDate == date);
        Assert.Equal(2, row.SchoolId);
        Assert.Equal(termId, row.TermId);
        Assert.Equal("A", row.AttendCode);
        Assert.Equal("moved", row.Notes);
        var summary = await db.StudentAttendanceSummaries.SingleAsync(item => item.StudentId == 5 && item.SchoolYear == "2026-2027");
        Assert.Equal(2, summary.SchoolId);
        Assert.Equal(0, summary.TotalAbsences);
    }

    [Theory]
    [InlineData(1, 99999)]
    [InlineData(99999, 1)]
    public async Task Save_WhenSchoolOrStudentCannotBeUsed_Returns404(int schoolId, int studentId)
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/attendance/daily", Batch(schoolId, new DateOnly(2026, 10, 24), null, Entry(studentId, "P", 0, null)));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task History_IsYearScoped_AndOrderedNewestFirst()
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();

        var current = await client.GetFromJsonAsync<AttendanceHistoryResponse>("/api/attendance/students/3/history?schoolYear=2026-2027");
        Assert.Equal(10, current!.Records.Count);
        Assert.Equal(new DateOnly(2026, 9, 10), current.Records[0].AttendDate);
        Assert.Equal(new DateOnly(2026, 9, 1), current.Records[^1].AttendDate);
        Assert.Equal("Absent", current.Records[0].Description);
        Assert.Equal("Fall 2026", current.Records[0].TermName);
        Assert.DoesNotContain(current.Records, record => record.AttendDate.Month < 9 && record.AttendDate.Year == 2026);

        var prior = await client.GetFromJsonAsync<AttendanceHistoryResponse>("/api/attendance/students/6/history?schoolYear=2025-2026");
        Assert.Equal(2, prior!.Records.Count);
        Assert.Equal(new DateOnly(2026, 5, 13), prior.Records[0].AttendDate);
        Assert.Equal(new DateOnly(2026, 5, 12), prior.Records[1].AttendDate);
        Assert.Equal("Spring 2026", prior.Records[0].TermName);

        var none = await client.GetFromJsonAsync<AttendanceHistoryResponse>("/api/attendance/students/6/history?schoolYear=2026-2027");
        Assert.Empty(none!.Records);
    }

    [Fact]
    public async Task History_WhenStudentHasNoRows_ReturnsEmptyList()
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var history = await client.GetFromJsonAsync<AttendanceHistoryResponse>("/api/attendance/students/1/history?schoolYear=2026-2027");
        Assert.Empty(history!.Records);
    }

    [Fact]
    public async Task History_WhenStudentMissing_ReturnsEmptyList()
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var history = await client.GetFromJsonAsync<AttendanceHistoryResponse>("/api/attendance/students/99999/history?schoolYear=2026-2027");
        Assert.Empty(history!.Records);
    }

    [Fact]
    public async Task History_WhenSchoolYearInvalid_Returns400()
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/attendance/students/1/history?schoolYear=2026-2028");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Chronic_WhenAtThreshold_ReturnsTrue()
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var status = await client.GetFromJsonAsync<ChronicAbsenteeismResponse>("/api/attendance/students/3/chronic-absenteeism?schoolYear=2026-2027");
        Assert.Equal(10, status!.TotalAbsences);
        Assert.Equal(10, status.AbsenceAlertThreshold);
        Assert.True(status.IsChronicallyAbsent);
    }

    [Fact]
    public async Task Chronic_WhenBelowThreshold_ReturnsFalse()
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var katherine = await client.GetFromJsonAsync<ChronicAbsenteeismResponse>("/api/attendance/students/5/chronic-absenteeism?schoolYear=2026-2027");
        Assert.Equal(2, katherine!.TotalAbsences);
        Assert.Equal(5, katherine.AbsenceAlertThreshold);
        Assert.False(katherine.IsChronicallyAbsent);

        var ada = await client.GetAsync("/api/attendance/students/1/chronic-absenteeism?schoolYear=2026-2027");
        Assert.Equal(HttpStatusCode.NotFound, ada.StatusCode);
    }

    [Fact]
    public async Task Chronic_WhenStudentMissing_Returns404()
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/attendance/students/99999/chronic-absenteeism?schoolYear=2026-2027");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Save_WhenFaultInjected_RollsBack()
    {
        await using var factory = new AttendanceApiFactory(throwBeforeCommit: true);
        var client = factory.CreateClient();
        var date = new DateOnly(2026, 12, 1);
        var response = await client.PostAsJsonAsync("/api/attendance/daily", Batch(1, date, "clerk", Entry(1, "A", 0, null)));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        Assert.False(await db.StudentAttendances.AnyAsync(row => row.AttendDate == date));
        Assert.False(await db.AttendanceSubmissionLogs.AnyAsync());
        Assert.False(await db.StudentAttendanceSummaries.AnyAsync(row => row.StudentId == 1));
    }

    [Fact]
    public async Task Save_WhenAlreadyChronic_DoesNotInsertASecondAlert()
    {
        await using var factory = new AttendanceApiFactory();
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/attendance/daily", Batch(1, new DateOnly(2026, 10, 21), null, Entry(3, "A", 0, null)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        Assert.Equal(11, await db.StudentAttendanceSummaries.Where(row => row.StudentId == 3 && row.SchoolYear == "2026-2027").Select(row => row.TotalAbsences).SingleAsync());
        Assert.Equal(1, await db.StudentAlerts.CountAsync(alert => alert.StudentId == 3 && alert.ResolvedDate == null));
    }

    [Fact]
    public async Task Schema_HasIndexesAndSeedData()
    {
        await using var factory = new AttendanceApiFactory();
        _ = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();

        Assert.Equal(3, await db.Schools.CountAsync());
        Assert.Equal(4, await db.AttendanceCodes.CountAsync());
        Assert.Equal(8, await db.SchoolTerms.CountAsync());
        Assert.Equal(7, await db.Students.CountAsync());
        Assert.Equal(10, await db.StudentAttendances.CountAsync(row => row.StudentId == 3 && row.IsAbsent));
        Assert.Equal(15, await db.StudentAttendances.CountAsync());
        Assert.Equal(4, await db.StudentAttendanceSummaries.CountAsync());
        Assert.Equal(1, await db.StudentAlerts.CountAsync());

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        var names = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index'";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        Assert.Contains("IX_Schools_Active_SchoolName", names);
        Assert.Contains("IX_SchoolTerms_School_Dates", names);
        Assert.Contains("IX_Students_School_Active_Grade", names);
        Assert.Contains("UX_AttendanceCodes_CodeValue", names);
        Assert.Contains("UX_StudentAttendance_StudentId_AttendDate", names);
        Assert.Contains("IX_StudentAttendance_Recount", names);
        Assert.Contains("UX_StudentAttendanceSummary_Student_Year", names);
        Assert.Contains("IX_StudentAlerts_OpenAlert", names);
    }

    private static SaveDailyAttendanceRequest Batch(int schoolId, DateOnly date, string? submittedBy, params AttendanceEntryRequest[] students) =>
        new()
        {
            SchoolId = schoolId,
            AttendDate = date,
            SubmittedBy = submittedBy,
            Students = students.ToList()
        };

    private static AttendanceEntryRequest Entry(int studentId, string code, int minutesLate, string? notes) =>
        new()
        {
            StudentId = studentId,
            AttendCode = code,
            MinutesLate = minutesLate,
            Notes = notes
        };

}
