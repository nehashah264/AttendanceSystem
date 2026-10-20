# Attendance System

.NET 8 monolith that replaces the legacy VB6 daily-attendance screen. The Legacy folder is left unchanged and is not referenced by this application. The analysis of that system is in `Legacy/docs/legacy-analysis.md` and `Legacy/docs/legacy-analysis-enhancement.md`. The build plan is in `docs/implementation-plan.md`.

## Architectural decisions

**One web project, split by folder.** Controllers, DTOs, services, validation, middleware, infrastructure, and domain live in `src/AttendanceSystem.Api`. Tests are separate projects. There is no MediatR and no separate application host. `Program.cs` composes dependency injection, creates the SQLite schema, and seeds an empty database.

**SQLite through EF Core, not SQL Server.** The API runs locally without a production database. The connection string is `Data Source=attendance.db`. Schema and indexes are created with `EnsureCreated` on startup. Foreign keys and the unique `(StudentId, AttendDate)` index stay in place.

**Three HTTP endpoints.**

- `POST /api/attendance/daily` saves one school-day for many students. A student with no row for that date is inserted and the response is `201`. A student who already has that date is updated, and the response is `200` when any row in the batch was updated. Each saved student includes `operation`: `Inserted` or `Updated`.
- `GET /api/attendance/students/{studentId}/history` returns that student's year, newest date first. No rows, including an unknown student id, returns an empty list.
- `GET /api/attendance/students/{studentId}/chronic-absenteeism` returns the stored absence total, the threshold from the school on that summary, and whether the student is chronically absent. No summary row returns `404`.

**Requests and responses are DTOs.** Controllers depend on `IAttendanceService`. The service depends on `IAttendanceStore` and `IUnitOfWork`. EF Core implements those interfaces. Domain entities stay free of HTTP types.

**Validation runs before any write.** FluentValidation validators are executed by `IValidationHandler`. Each service method calls that handler first. Failures become `400` problem details that include the field errors.

**One transaction for the whole batch.** Attendance rows, the year summary, a chronic alert, and the submission log commit together. Any failure rolls all of them back.

**Errors are mapped in middleware.** Validation is `400`. A school or student id that is not in the database is `404`. A code that is not in `AttendanceCodes` is `400`. Anything else is `500`. The `500` body is a generic problem result. The stack trace stays in the server log. A unique-index race on the same student and date can still surface as `409` if two requests insert that day at the same time.

**Swagger is Development-only.** It is registered so the API can be tried locally at `/swagger`.

**The chronic-absence fallback is configuration.** `Attendance:ChronicAbsenceDays` in `appsettings.json` and `appsettings.Development.json` defaults to 10. A school that stores `AbsenceAlertThreshold` uses that number instead. Changing the fallback does not require a code change.

**Save follows the procedure's insert and update.** A new day stores the request school and the term that covers the date. An existing day updates the code, absence flags, minutes, notes, `ModifiedDate`, and `ModifiedBy`. School and term on that row stay as they were. A later save updates `TotalAbsences` and `LastUpdated` on the year summary and leaves `SchoolId` as it was on the first insert. `CreatedBy`, `ModifiedBy`, and the submission log store `SYSTEM_USER`.

## Ambiguities the legacy system did not settle

These are places where the VB6 form or the SQL scripts were missing, silent, or contradictory. The API takes an explicit position on each one.

| What legacy left open | How this API handles it |
| --- | --- |
| `Schools`, `SchoolTerms`, and `AttendanceSubmissionLog` are used and never created. `sp_GetStudentsForAttendance` has no body. | Those three tables are created here, with indexes. There is no roster endpoint, because that procedure was never defined. |
| `SchoolYear(@AttendDate)` is called and never created. The save passes the batch date, so the predicate does not limit the recount to one year. The history procedure computes the year from each row. | The school year is September through August. Month 9 or later is `YYYY-(YYYY+1)`; otherwise it is `(YYYY-1)-YYYY`. The recount and the history query both filter `AttendDate` to that range. |
| The save cursor leaves `@ExistingID` set. A student with no row can overwrite the previous student's row. | Each entry looks up its own student and date. A repeated student in one request is applied in order, and the last code, minutes, and notes are stored. The log counts every entry. The response lists that student once. |
| Overlapping terms have no `ORDER BY`. A date with no term leaves `TermId` null. | The lowest `TermId` whose dates cover the attendance date is stored. No match leaves `TermId` null. |
| The form shows only active schools and students. The save procedure accepts an inactive school, an inactive student, and a student from another school. The schema has no foreign keys, so an unknown id is still inserted. | Existing inactive schools, inactive students, and students from another school are saved. An id that is not in `Schools` or `Students` is `404`, so the foreign keys already on these tables are not violated. |
| An unknown attendance code is stored as present. `IsActive` is ignored. No code rows are seeded, while the form defaults a blank cell to `P`. | Codes `P`, `A`, `E`, and `T` are seeded. A blank code is saved as `P`. An inactive code is accepted and its `IsAbsent` and `IsExcused` values are copied. A code that is not in `AttendanceCodes` is `400`. |
| Excused absences are included in the absence count, with no statement that this was intentional. A tardy is stored and is not an absence. | Excused absences still count. A tardy (`T`) does not. |
| Chronic status is `total >= threshold`, while the alert row is inserted once and never resolved. The default threshold `10` is hard-coded. History and chronic status are two result sets, and the second set is empty when no summary exists. | Status is the comparison, using the school stored on the summary. The alert is inserted once and is not auto-resolved. The hard-coded `10` is `Attendance:ChronicAbsenceDays`, used only when the school threshold is null. An empty history is `200` with no records. An empty chronic result is `404`. |
| `CreatedBy` is the SQL login `SYSTEM_USER`. The form has no signed-in user. | `CreatedBy`, `ModifiedBy`, and the submission log store `SYSTEM_USER`. `SubmittedBy` on the request is ignored. There is no authentication. |
| The form can only post today's date, and the grid has no edit overlay. | The API accepts any attendance date from the year 2000 onward. The client sends the code, minutes, and notes. |
| There is no transaction. A failure mid-batch leaves a partial save and no submission log. | The batch is one transaction. |

## Run locally

```powershell
dotnet run --project src/AttendanceSystem.Api --launch-profile http
```

Open `http://localhost:5155/swagger`. The database file is created under `src/AttendanceSystem.Api`. Seeded schools are Lincoln Elementary (1, threshold 10), Riverside High (2, threshold 5), and Closed Academy (3, inactive). Seeded students include Ada Lovelace (1), Alan Turing (2), and Grace Hopper (3).

```powershell
dotnet test AttendanceSystem.slnx
```
