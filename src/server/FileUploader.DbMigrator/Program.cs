using FileUploader.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                context.Configuration.GetConnectionString("postgresdb")));
    })
    .Build();

return await MigrateWithRetryAsync(host.Services);

static async Task<int> MigrateWithRetryAsync(IServiceProvider sp)
{
    var logger = sp.GetRequiredService<ILogger<Program>>();
    var db = sp.GetRequiredService<AppDbContext>();

    for (var i = 0; i < 10; i++)
    {
        try
        {
            logger.LogInformation("Attempting database migration...");
            await db.Database.MigrateAsync();
            logger.LogInformation("Database migration succeeded.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Migration attempt {Attempt} failed. Retrying...", i + 1);
            await Task.Delay(5000);
        }
    }

    return 1;
}
