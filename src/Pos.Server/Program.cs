using Pos.Server.Hosting;
using Pos.Server.Services;

var port = int.TryParse(Environment.GetEnvironmentVariable("MODERNPOS_PORT"), out var parsed) ? parsed : 5050;
var defaultConnectionString = $"Data Source={ServerStoragePaths.DefaultDatabasePath}";
var conn = Environment.GetEnvironmentVariable("MODERNPOS_CONN") ?? defaultConnectionString
var company = Environment.GetEnvironmentVariable("MODERNPOS_COMPANY") ?? "Unconfigured";

var host = await ModernPosServerHost.StartAsync(new ModernPosServerOptions(conn, port, company));
await host.WaitForShutdownAsync();
