using Asp.Versioning;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using SNHub.Jobs.API.Middleware;
using SNHub.Jobs.Infrastructure.Extensions;
using SNHub.Jobs.Infrastructure.Persistence;
using System.Text;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext().WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));

    var appInsights = builder.Configuration["ApplicationInsights:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(appInsights))
        builder.Services.AddApplicationInsightsTelemetry(o => o.ConnectionString = appInsights);

    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(SNHub.Jobs.Application.Commands.CreateJob.CreateJobCommand).Assembly));
    builder.Services.AddValidatorsFromAssembly(typeof(SNHub.Jobs.Application.Commands.CreateJob.CreateJobCommandValidator).Assembly);
    builder.Services.AddControllers();
    builder.Services.AddApiVersioning(o => { o.DefaultApiVersion = new ApiVersion(1, 0); o.AssumeDefaultVersionWhenUnspecified = true; })
        .AddApiExplorer(o => { o.GroupNameFormat = "'v'VVV"; o.SubstituteApiVersionInUrl = true; });

    var jwt = builder.Configuration.GetSection("JwtSettings");
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"], ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey required."))),
            ClockSkew = TimeSpan.FromSeconds(30)
        });
    builder.Services.AddAuthorization();

    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000"];
    builder.Services.AddCors(o => o.AddPolicy("SNHubCors", p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
    builder.Services.AddOpenApi("v1");
    builder.Services.AddHealthChecks();

    var app = builder.Build();
    if (app.Configuration.GetValue<bool>("RunMigrationsOnStartup"))
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<JobsDbContext>().Database.MigrateAsync();
        Log.Information("Jobs DB migrated.");
    }

    app.UseMiddleware<ExceptionMiddleware>();
    app.UseSerilogRequestLogging();
    if (app.Environment.IsDevelopment()) { app.MapOpenApi(); app.MapScalarApiReference(o => { o.Title = "SNHub Jobs API"; o.Theme = ScalarTheme.DeepSpace; }); }
    app.UseHttpsRedirection();
    app.UseCors("SNHubCors");
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.MapHealthChecks("/health");
    Log.Information("SNHub Jobs Service ready on {Env}", app.Environment.EnvironmentName);
    await app.RunAsync();
}
catch (Exception ex) { Log.Fatal(ex, "Jobs Service failed to start."); throw; }
finally { Log.CloseAndFlush(); }
