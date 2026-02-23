using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using SNHub.Notifications.API.Middleware;
using SNHub.Notifications.Infrastructure.Extensions;
using SNHub.Notifications.Infrastructure.Persistence;
using System.Text;

Log.Logger = new LoggerConfiguration().MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext().WriteTo.Console().CreateBootstrapLogger();
try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext().WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(SNHub.Notifications.Application.Commands.CreateNotification.CreateNotificationCommand).Assembly));
    builder.Services.AddControllers();
    builder.Services.AddApiVersioning(o => { o.DefaultApiVersion = new ApiVersion(1, 0); o.AssumeDefaultVersionWhenUnspecified = true; }).AddApiExplorer(o => { o.GroupNameFormat = "'v'VVV"; o.SubstituteApiVersionInUrl = true; });
    var jwt = builder.Configuration.GetSection("JwtSettings");
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true, ValidIssuer = jwt["Issuer"], ValidAudience = jwt["Audience"], IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["SecretKey"]!)), ClockSkew = TimeSpan.FromSeconds(30) });
    builder.Services.AddAuthorization();
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000"];
    builder.Services.AddCors(o => o.AddPolicy("SNHubCors", p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
    builder.Services.AddOpenApi("v1");
    builder.Services.AddHealthChecks();
    var app = builder.Build();
    if (app.Configuration.GetValue<bool>("RunMigrationsOnStartup")) { using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync(); }
    app.UseMiddleware<ExceptionMiddleware>(); app.UseSerilogRequestLogging();
    if (app.Environment.IsDevelopment()) { app.MapOpenApi(); app.MapScalarApiReference(o => { o.Title = "SNHub Notifications API"; o.Theme = ScalarTheme.DeepSpace; }); }
    app.UseHttpsRedirection(); app.UseCors("SNHubCors"); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); app.MapHealthChecks("/health");
    await app.RunAsync();
}
catch (Exception ex) { Log.Fatal(ex, "Notifications Service failed."); throw; }
finally { Log.CloseAndFlush(); }
