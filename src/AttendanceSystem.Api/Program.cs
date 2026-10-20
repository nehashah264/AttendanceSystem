using AttendanceSystem.Api;
using AttendanceSystem.Api.Infrastructure;
using AttendanceSystem.Api.Infrastructure.Persistence;
using AttendanceSystem.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAttendanceApplication(builder.Configuration);
builder.Services.AddAttendanceInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
    await db.Database.EnsureCreatedAsync();
    await AttendanceSeeder.SeedAsync(db);
}

app.Run();

public partial class Program;
