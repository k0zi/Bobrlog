using Bobrlog.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSystemd();
builder.Services.AddHostedService<SocketServer>();

var host = builder.Build();
host.Run();
