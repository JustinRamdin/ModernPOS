using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pos.Application.Auth;
using Pos.Infrastructure.Auth;
using Pos.Infrastructure.Data;
using Pos.Server.Auth;
using Pos.Server.Data;
using Pos.Server.Discovery;

namespace Pos.Server.Hosting;

public sealed record ModernPosServerOptions(string ConnectionString, int Port, string CompanyName);

public static class ModernPosServerHost
{
    public static async Task<IHost> StartAsync(ModernPosServerOptions options, CancellationToken ct = default)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{options.Port}");

        builder.Services.AddControllers();
        builder.Services.AddOpenApi();
        builder.Services.AddDbContext<PosDbContext>(x => x.UseSqlite(options.ConnectionString));
        builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        builder.Services.AddSingleton<ISessionTokenStore, InMemorySessionTokenStore>();
        builder.Services.AddSingleton(new LanAdvertiserOptions { CompanyName = options.CompanyName, ServerPort = options.Port });
        builder.Services.AddHostedService<LanAdvertiserHostedService>();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
            await Seeder.SeedAsync(db);
        }

        app.UseMiddleware<SessionAuthMiddleware>();
        app.MapControllers();
        app.MapGet("/health", () => Results.Ok(new { status = "ok", timeUtc = DateTime.UtcNow }));

        await app.StartAsync(ct);
        return app;
    }
}
