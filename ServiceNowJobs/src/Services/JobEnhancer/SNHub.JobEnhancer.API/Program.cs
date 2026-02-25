using Asp.Versioning;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using SNHub.JobEnhancer.API.Middleware;
using SNHub.JobEnhancer.Application.Behaviors;
using SNHub.JobEnhancer.Infrastructure.Extensions;
using SNHub.JobEnhancer.Infrastructure.Persistence;
using System.Text;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext().WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    builder.Services.AddInfrastructure(builder.Configuration);

    builder.Services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssembly(
            typeof(SNHub.JobEnhancer.Application.Commands.EnhanceDescription
                .EnhanceDescriptionCommand).Assembly);
        cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
    });
    builder.Services.AddValidatorsFromAssembly(
        typeof(SNHub.JobEnhancer.Application.Commands.EnhanceDescription
            .EnhanceDescriptionCommandValidator).Assembly);

    builder.Services.AddControllers();
    builder.Services
        .AddApiVersioning(o =>
        {
            o.DefaultApiVersion = new ApiVersion(1, 0);
            o.AssumeDefaultVersionWhenUnspecified = true;
        })
        .AddApiExplorer(o =>
        {
            o.GroupNameFormat           = "'v'VVV";
            o.SubstituteApiVersionInUrl = true;
        });

    var jwt = builder.Configuration.GetSection("JwtSettings");
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwt["Issuer"],
            ValidAudience            = jwt["Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt["SecretKey"]
                    ?? throw new InvalidOperationException("JwtSettings:SecretKey required."))),
            ClockSkew = TimeSpan.FromSeconds(30)
        });
    builder.Services.AddAuthorization();

    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
               ?? ["http://localhost:3000"];
    builder.Services.AddCors(o =>
        o.AddPolicy("SNHubCors", p =>
            p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

    builder.Services.AddOpenApi("v1");
    builder.Services.AddHealthChecks();

    var app = builder.Build();

    if (app.Configuration.GetValue<bool>("RunMigrationsOnStartup"))
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<JobEnhancerDbContext>().Database.MigrateAsync();
        Log.Information("JobEnhancer DB migrated.");
    }

    app.UseMiddleware<ExceptionMiddleware>();
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
    {
        app.MapOpenApi();
        app.MapScalarApiReference(o =>
        {
            o.Title = "SNHub Job Enhancer API";
            o.Theme = ScalarTheme.DeepSpace;
        });
    }

    if (!app.Environment.IsEnvironment("Testing"))
        app.UseHttpsRedirection();

    app.UseCors("SNHubCors");
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.MapHealthChecks("/health");

    Log.Information("SNHub Job Enhancer Service ready on {Env}", app.Environment.EnvironmentName);
    await app.RunAsync();
}
catch (Exception ex) { Log.Fatal(ex, "Job Enhancer Service failed to start."); throw; }
finally { Log.CloseAndFlush(); }

public partial class Program { }
