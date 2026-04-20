using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServantClaw.Host;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
