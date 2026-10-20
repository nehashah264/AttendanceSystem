using AttendanceSystem.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Api.Infrastructure.Persistence;

public sealed class AttendanceDbContext : DbContext
{
    public AttendanceDbContext(DbContextOptions<AttendanceDbContext> options)
        : base(options)
    {
    }

    public DbSet<School> Schools => Set<School>();

    public DbSet<SchoolTerm> SchoolTerms => Set<SchoolTerm>();

    public DbSet<Student> Students => Set<Student>();

    public DbSet<AttendanceCode> AttendanceCodes => Set<AttendanceCode>();

    public DbSet<StudentAttendance> StudentAttendances => Set<StudentAttendance>();

    public DbSet<StudentAttendanceSummary> StudentAttendanceSummaries => Set<StudentAttendanceSummary>();

    public DbSet<StudentAlert> StudentAlerts => Set<StudentAlert>();

    public DbSet<AttendanceSubmissionLog> AttendanceSubmissionLogs => Set<AttendanceSubmissionLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<School>(entity =>
        {
            entity.ToTable("Schools");
            entity.HasKey(school => school.SchoolId);
            entity.Property(school => school.SchoolName).HasMaxLength(200).IsRequired();
            entity.HasIndex(school => new { school.Active, school.SchoolName })
                .HasDatabaseName("IX_Schools_Active_SchoolName");
        });

        modelBuilder.Entity<SchoolTerm>(entity =>
        {
            entity.ToTable("SchoolTerms");
            entity.HasKey(term => term.TermId);
            entity.Property(term => term.TermName).HasMaxLength(100).IsRequired();
            entity.HasIndex(term => new { term.SchoolId, term.StartDate, term.EndDate })
                .HasDatabaseName("IX_SchoolTerms_School_Dates");
            entity.HasOne<School>()
                .WithMany()
                .HasForeignKey(term => term.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Student>(entity =>
        {
            entity.ToTable("Students");
            entity.HasKey(student => student.StudentId);
            entity.Property(student => student.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(student => student.LastName).HasMaxLength(100).IsRequired();
            entity.Property(student => student.Grade).HasMaxLength(10);
            entity.HasIndex(student => new { student.SchoolId, student.Active, student.Grade })
                .HasDatabaseName("IX_Students_School_Active_Grade");
            entity.HasOne(student => student.School)
                .WithMany()
                .HasForeignKey(student => student.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AttendanceCode>(entity =>
        {
            entity.ToTable("AttendanceCodes");
            entity.HasKey(code => code.CodeId);
            entity.Property(code => code.CodeValue).HasMaxLength(5).IsRequired();
            entity.Property(code => code.Description).HasMaxLength(100).IsRequired();
            entity.HasIndex(code => code.CodeValue)
                .IsUnique()
                .HasDatabaseName("UX_AttendanceCodes_CodeValue");
            entity.HasAlternateKey(code => code.CodeValue);
        });

        modelBuilder.Entity<StudentAttendance>(entity =>
        {
            entity.ToTable("StudentAttendance");
            entity.HasKey(row => row.AttendanceId);
            entity.Property(row => row.AttendCode).HasMaxLength(5).IsRequired();
            entity.Property(row => row.Notes).HasMaxLength(500);
            entity.Property(row => row.CreatedBy).HasMaxLength(100);
            entity.Property(row => row.ModifiedBy).HasMaxLength(100);
            entity.HasIndex(row => new { row.StudentId, row.AttendDate })
                .IsUnique()
                .HasDatabaseName("UX_StudentAttendance_StudentId_AttendDate");
            entity.HasIndex(row => new { row.StudentId, row.SchoolId, row.AttendDate })
                .HasDatabaseName("IX_StudentAttendance_Recount");
            entity.HasOne<Student>()
                .WithMany()
                .HasForeignKey(row => row.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<School>()
                .WithMany()
                .HasForeignKey(row => row.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SchoolTerm>()
                .WithMany()
                .HasForeignKey(row => row.TermId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AttendanceCode>()
                .WithMany()
                .HasForeignKey(row => row.AttendCode)
                .HasPrincipalKey(code => code.CodeValue)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StudentAttendanceSummary>(entity =>
        {
            entity.ToTable("StudentAttendanceSummary");
            entity.HasKey(summary => summary.SummaryId);
            entity.Property(summary => summary.SchoolYear).HasMaxLength(9).IsRequired();
            entity.HasIndex(summary => new { summary.StudentId, summary.SchoolYear })
                .IsUnique()
                .HasDatabaseName("UX_StudentAttendanceSummary_Student_Year");
            entity.HasOne<Student>()
                .WithMany()
                .HasForeignKey(summary => summary.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<School>()
                .WithMany()
                .HasForeignKey(summary => summary.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StudentAlert>(entity =>
        {
            entity.ToTable("StudentAlerts");
            entity.HasKey(alert => alert.AlertId);
            entity.Property(alert => alert.AlertType).HasMaxLength(50).IsRequired();
            entity.Property(alert => alert.SchoolYear).HasMaxLength(9).IsRequired();
            entity.Property(alert => alert.AlertMessage).HasMaxLength(500);
            entity.Property(alert => alert.ResolvedBy).HasMaxLength(100);
            entity.HasIndex(alert => new { alert.StudentId, alert.SchoolYear, alert.AlertType })
                .HasDatabaseName("IX_StudentAlerts_OpenAlert");
            entity.HasOne<Student>()
                .WithMany()
                .HasForeignKey(alert => alert.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<School>()
                .WithMany()
                .HasForeignKey(alert => alert.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AttendanceSubmissionLog>(entity =>
        {
            entity.ToTable("AttendanceSubmissionLog");
            entity.HasKey(log => log.LogId);
            entity.Property(log => log.SubmittedBy).HasMaxLength(100);
            entity.HasOne<School>()
                .WithMany()
                .HasForeignKey(log => log.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
