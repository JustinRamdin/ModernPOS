using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure.Data;
using Pos.Server.Data;


var builder = WebApplication.CreateBuilder(args);

// =======================
// Services
// =======================

// Controllers (API endpoints)
builder.Services.AddControllers();

// OpenAPI / Swagger
builder.Services.AddOpenApi();

// PostgreSQL + EF Core
builder.Services.AddDbContext<PosDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("PosDb")
    )
);

// =======================
// Build app
// =======================

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
    await Seeder.SeedAsync(db);
}

// =======================
// Middleware pipeline
// =======================

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();   // Swagger / OpenAPI
}

// NOTE: Disabled for LAN dev (no HTTPS cert yet)
// app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

// =======================
// Endpoints
// =======================

app.MapControllers();

// Simple health check (VERY important for installers & clients)
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    timeUtc = DateTime.UtcNow
}));

app.Run();
