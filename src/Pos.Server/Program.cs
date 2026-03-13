using Pos.Server.Hosting;

var port = int.TryParse(Environment.GetEnvironmentVariable("MODERNPOS_PORT"), out var parsed) ? parsed : 5050;
var conn = Environment.GetEnvironmentVariable("MODERNPOS_CONN") ?? "Data Source=modernpos.server.db";
var company = Environment.GetEnvironmentVariable("MODERNPOS_COMPANY") ?? "Unconfigured";

var host = await ModernPosServerHost.StartAsync(new ModernPosServerOptions(conn, port, company));
await host.WaitForShutdownAsync();
