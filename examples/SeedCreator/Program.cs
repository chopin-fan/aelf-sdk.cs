﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SeedCreator;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Volo.Abp", LogEventLevel.Warning)
#if DEBUG
    .MinimumLevel.Override("SeedCreator", LogEventLevel.Debug)
#else
    .MinimumLevel.Override("ProfitClaimer", LogEventLevel.Information)
#endif
    .Enrich.FromLogContext()
    .WriteTo.Async(c => c.File($"Logs/aelf-seed-creator-{DateTime.UtcNow:yyyy-MM-dd}.logs"))
    .WriteTo.Async(c => c.Console())
    .CreateLogger();

try
{
    await Host.CreateDefaultBuilder(args)
        .ConfigureServices(services =>
        {
            services.AddHostedService<SeedCreatorHostedService>();
        })
        .UseSerilog()
        .RunConsoleAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly!");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}