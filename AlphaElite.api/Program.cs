using AlphaElite.domain.entities;
using AlphaElite.infrastructure.data;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Use builder.Configuration provided by the host (no need to create separate IConfiguration here)

// Add services to the container.
// The master context uses the SAME resilient configuration as the desktop application's master
// contexts (bounded transient-failure retry). The tenant context below is deliberately untouched.
builder.Services.AddDbContext<MasterAlphaEliteDbContext>(options =>
  options.UseSqlServer(
    builder.Configuration.GetConnectionString("MasterErp"),
    MasterDbContextOptions.Configure));

builder.Services.AddDbContext<TenantAlphaEliteDbContext>(options =>
  options.UseSqlServer(
    builder.Configuration.GetConnectionString("TenantErp")));

builder.Services.AddScoped<ITenantDatabaseResolver, TenantDatabaseResolver>();

// Resolves tenant connection strings from configured TenantCredentials (the credential
// key in the master database is a lookup key, not a database login).
builder.Services.AddSingleton<TenantDbConnectionFactory>();

builder.Services.AddScoped<ITenantDbContextFactory, TenantDbContextFactory>();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();  

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapPost("/companies", async (
  Company company,
  MasterAlphaEliteDbContext db) =>
{
    db.Companies.Add(company);
    await db.SaveChangesAsync();
    return Results.Created($"/companies/{company.CompanyId}", company);

});
app.MapPost("/devices", async (
  Device device,
  MasterAlphaEliteDbContext db) =>
{
    db.Devices.Add(device);
    await db.SaveChangesAsync();
    return Results.Created($"/devices/{device.DeviceId}", device);
});
app.MapPost("/company-databases", async (

  CompanyDatabase companyDatabase,
  MasterAlphaEliteDbContext db) =>
{
    db.CompanyDatabase.Add(companyDatabase);
    await db.SaveChangesAsync();

    return Results.Created(
      $"/company-databases/{companyDatabase.CompanyDatabaseId}",
      companyDatabase);
});

app.MapGet("/test-tenant/{companyId:int}", async (
  int companyId,
  ITenantDbContextFactory tenantFactory) =>
{
    await using var tenantDb = await tenantFactory.CreateAsync(companyId);

    var memberCount = await tenantDb.Members.CountAsync();

    return Results.Ok(new
    {
        companyId,
        memberCount
    });
});

app.MapPost("/tenant/{companyId:int}/members", async (
  int companyId,
  Member member,
  ITenantDbContextFactory tenantFactory) =>
{
    await using var tenantDb = await tenantFactory.CreateAsync(companyId);

    tenantDb.Members.Add(member);
    await tenantDb.SaveChangesAsync();

    return Results.Created(
      $"/tenant/{companyId}/members/{member.MemberId}",
      member);
});

app.MapGet("/tenant/{companyId:int}/members", async (
  int companyId,
  ITenantDbContextFactory tenantFactory) =>
{
    await using var tenantDb = await tenantFactory.CreateAsync(companyId);

    var members = await tenantDb.Members
      .AsNoTracking()
      .OrderBy(x => x.MemberId)
      .ToListAsync();

    return Results.Ok(members);
});

app.MapPost("/tenant/{companyId:int}/attendance", async (
  int companyId,
  Attendance attendance,
  ITenantDbContextFactory tenantFactory) =>
{
    await using var tenantDb = await tenantFactory.CreateAsync(companyId);

    tenantDb.Attendances.Add(attendance);
    await tenantDb.SaveChangesAsync();

    return Results.Created(
      $"/tenant/{companyId}/attendance/{attendance.AttendanceId}",
      attendance);
});

app.MapGet("/tenant/{companyId:int}/attendance", async (
  int companyId,
  ITenantDbContextFactory tenantFactory) =>
{
    await using var tenantDb = await tenantFactory.CreateAsync(companyId);

    var attendanceRecords = await tenantDb.Attendances
      .AsNoTracking()
      .OrderByDescending(x => x.CheckInDate)
      .ToListAsync();

    return Results.Ok(attendanceRecords);
});

app.MapPost("/tenant/{companyId:int}/payments", async (
  int companyId,
  Payment payment,
  ITenantDbContextFactory tenantFactory) =>
{
    await using var tenantDb = await tenantFactory.CreateAsync(companyId);

    tenantDb.Payments.Add(payment);
    await tenantDb.SaveChangesAsync();

    return Results.Created(
      $"/tenant/{companyId}/payments/{payment.PaymentId}",
      payment);
});

app.MapGet("/tenant/{companyId:int}/payments", async (
  int companyId,
  ITenantDbContextFactory tenantFactory) =>
{
    await using var tenantDb = await tenantFactory.CreateAsync(companyId);

    var payments = await tenantDb.Payments
      .AsNoTracking()
      .OrderByDescending(x => x.PaymentDate)
      .ToListAsync();

    return Results.Ok(payments);
});

app.MapPost("/tenant/{companyId:int}/inquiries", async (
  int companyId,
  Inquiry inquiry,
  ITenantDbContextFactory tenantFactory) =>
{
    await using var tenantDb = await tenantFactory.CreateAsync(companyId);

    tenantDb.Inquiries.Add(inquiry);
    await tenantDb.SaveChangesAsync();

    return Results.Created(
      $"/tenant/{companyId}/inquiries/{inquiry.InquiryId}",
      inquiry);
});

app.MapGet("/tenant/{companyId:int}/inquiries", async (
  int companyId,
  ITenantDbContextFactory tenantFactory) =>
{
    await using var tenantDb = await tenantFactory.CreateAsync(companyId);

    var inquiries = await tenantDb.Inquiries
      .AsNoTracking()
      .OrderByDescending(x => x.CreatedAt)
      .ToListAsync();

    return Results.Ok(inquiries);
});

app.MapPost("/tenant/{companyId:int}/feedback", async (
  int companyId,
  Feedback feedback,
  ITenantDbContextFactory tenantFactory) =>
{
    await using var tenantDb = await tenantFactory.CreateAsync(companyId);

    tenantDb.Feedbacks.Add(feedback);
    await tenantDb.SaveChangesAsync();

    return Results.Created(
      $"/tenant/{companyId}/feedback/{feedback.FeedbackId}",
      feedback);
});

app.MapGet("/tenant/{companyId:int}/feedback", async (
  int companyId,
  ITenantDbContextFactory tenantFactory) =>
{
    await using var tenantDb = await tenantFactory.CreateAsync(companyId);

    var feedbackList = await tenantDb.Feedbacks
      .AsNoTracking()
      .OrderByDescending(x => x.SubmittedAt)
      .ToListAsync();

    return Results.Ok(feedbackList);
});

app.Run();


