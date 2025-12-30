using FileUploader.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(config =>
    {
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                context.Configuration.GetConnectionString("DefaultConnection")));
    })
    .Build();

Console.WriteLine("Applying EF Core migrations...");

using var scope = host.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

await db.Database.MigrateAsync();

Console.WriteLine("Migrations applied successfully.");