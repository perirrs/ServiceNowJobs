using Asp.Versioning;
using Azure.Identity;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using SNHub.Auth.API.Middleware;
using SNHub.Auth.API.Services;
using SNHub.Auth.Application.Behaviors;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Infrastructure.Extensions;
using SNHub.Auth.Infrastructure.Services;
using System.Text;
using System.Threading.RateLimiting;

// ─── Bootstrap logger ────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting SNHub Auth Service");

    var builder = WebApplication.CreateBuilder(args);

    // ─── Azure Key Vault (production secrets — no secrets in config files) ────
    var keyVaultUrl = builder.Configuration["Azure:KeyVaultUrl"];
    if (!string.IsNullOrWhiteSpace(keyVaultUrl) && builder.Environment.IsProduction())
    {
        builder.Configuration.AddAzureKeyVault(
            new Uri(keyVaultUrl),
            new DefaultAzureCredential());
        Log.Information("Azure Key Vault connected: {Url}", keyVaultUrl);
    }

    // ─── Serilog with Azure Application Insights ─────────────────────────────
    builder.Host.UseSerilog((ctx, services, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithThreadId()
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.ApplicationInsights(
            services.GetRequiredService<Microsoft.ApplicationInsights.TelemetryClient>(),
            TelemetryConverter.Traces));

    // ─── Azure Application Insights ───────────────────────────────────────────
    builder.Services.AddApplicationInsightsTelemetry(opts =>
    {
        opts.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
    });

    // ─── Infrastructure (PostgreSQL, Redis, Blob, Services) ──────────────────
    builder.Services.AddInfrastructure(builder.Configuration);

    // ─── MediatR + FluentValidation + Pipeline Behaviors ─────────────────────
    builder.Services.AddMediatR(cfg =>
        cfg.RegisterServicesFromAssembly(
            typeof(SNHub.Auth.Application.Commands.RegisterUser.RegisterUserCommand).Assembly));

    builder.Services.AddValidatorsFromAssembly(
        typeof(SNHub.Auth.Application.Commands.RegisterUser.RegisterUserCommandValidator).Assembly);

    builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
    builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

    // ─── Controllers ─────────────────────────────────────────────────────────
    builder.Services.AddControllers()
        .AddJsonOptions(opts =>
        {
            opts.JsonSerializerOptions.PropertyNamingPolicy =
                System.Text.Json.JsonNamingPolicy.CamelCase;
            opts.JsonSerializerOptions.DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        });

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

    // ─── API Versioning ───────────────────────────────────────────────────────
    builder.Services.AddApiVersioning(opts =>
    {
        opts.DefaultApiVersion = new ApiVersion(1, 0);
        opts.AssumeDefaultVersionWhenUnspecified = true;
        opts.ReportApiVersions = true;
        opts.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            new HeaderApiVersionReader("X-Api-Version"));
    }).AddApiExplorer(opts =>
    {
        opts.GroupNameFormat = "'v'VVV";
        opts.SubstituteApiVersionInUrl = true;
    });

    // ─── JWT Authentication ───────────────────────────────────────────────────
    var jwt = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
        ?? throw new InvalidOperationException("JwtSettings not configured.");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opts =>
        {
            opts.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwt.SecretKey)),
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        });

    builder.Services.AddAuthorization(opts =>
    {
        opts.AddPolicy("AdminOnly", p => p.RequireRole("SuperAdmin"));
        opts.AddPolicy("ModeratorOrAdmin", p => p.RequireRole("SuperAdmin", "Moderator"));
        opts.AddPolicy("EmployerOrAdmin", p => p.RequireRole("SuperAdmin", "Employer", "HiringManager"));
        opts.AddPolicy("EmailVerified", p =>
            p.RequireAuthenticatedUser().RequireClaim("email_verified", "true"));
    });

    // ─── Rate Limiting ────────────────────────────────────────────────────────
    builder.Services.AddRateLimiter(opts =>
    {
        opts.RejectionStatusCode = 429;

        opts.AddFixedWindowLimiter("registration", o =>
        {
            o.PermitLimit = 3; o.Window = TimeSpan.FromHours(1); o.QueueLimit = 0;
        });
        opts.AddFixedWindowLimiter("login", o =>
        {
            o.PermitLimit = 10; o.Window = TimeSpan.FromMinutes(15); o.QueueLimit = 0;
        });
        opts.AddFixedWindowLimiter("token", o =>
        {
            o.PermitLimit = 30; o.Window = TimeSpan.FromMinutes(1); o.QueueLimit = 0;
        });
        opts.AddFixedWindowLimiter("passwordReset", o =>
        {
            o.PermitLimit = 3; o.Window = TimeSpan.FromHours(1); o.QueueLimit = 0;
        });
    });

    // ─── CORS ─────────────────────────────────────────────────────────────────
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins")
        .Get<string[]>() ?? ["http://localhost:3000"];

    builder.Services.AddCors(opts => opts.AddPolicy("SNHubCors", p =>
        p.WithOrigins(origins)
         .AllowAnyHeader()
         .AllowAnyMethod()
         .AllowCredentials()));

    // ─── OpenAPI ──────────────────────────────────────────────────────────────
    builder.Services.AddOpenApi("v1", opts =>
    {
        opts.AddDocumentTransformer((doc, ctx, ct) =>
        {
            doc.Info = new()
            {
                Title = "SNHub Auth API",
                Version = "v1",
                Description = "Authentication service for SNHub — ServiceNow Talent Platform. Hosted on Azure."
            };
            return Task.CompletedTask;
        });
    });

    // ─── Health Checks ────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks();

    // ─── Build app ────────────────────────────────────────────────────────────
    var app = builder.Build();

    // Auto-migrate and seed on startup when enabled (dev/staging only)
    if (app.Configuration.GetValue<bool>("RunMigrationsOnStartup"))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<SNHub.Auth.Infrastructure.Persistence.AuthDbContext>();

        Log.Information("Applying database migrations...");
        await db.Database.MigrateAsync();
        Log.Information("Migrations applied.");

        Log.Information("Running database seeder...");
        var seeder = scope.ServiceProvider
            .GetRequiredService<SNHub.Auth.Infrastructure.Persistence.AuthDbSeeder>();
        await seeder.SeedAsync();
        Log.Information("Seeding complete.");
    }

    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseRateLimiter();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference(opts =>
        {
            opts.Title = "SNHub Auth API";
            opts.Theme = ScalarTheme.DeepSpace;
        });
    }

    app.UseHttpsRedirection();
    app.UseCors("SNHubCors");
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/ready",
        new() { Predicate = c => c.Tags.Contains("ready") });
    app.MapHealthChecks("/health/live",
        new() { Predicate = _ => false });

    Log.Information("SNHub Auth Service ready. Environment: {Env}", app.Environment.EnvironmentName);
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Auth Service failed to start.");
}
finally
{
    Log.CloseAndFlush();
}
