using FileUploader.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FileUploader.ApiService.Middlewares;

public class EnsureUserExistsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public EnsureUserExistsMiddleware(RequestDelegate next, IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _next = next;
        _dbContextFactory = dbContextFactory;
    }

    public async Task InvokeAsync(HttpContext context, IServiceProvider services)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirst("sub")?.Value;

            if (!string.IsNullOrEmpty(userId))
            {
                using var db = _dbContextFactory.CreateDbContext();
                var exists = await db.Users.AnyAsync(u => u.Sub == userId);

                if (!exists)
                {
                    try 
                    {
                        db.Users.Add(new User
                        {
                            Sub = userId,
                        });
                        await db.SaveChangesAsync(); 
                    }
                    catch (DbUpdateException) 
                    {
                        // Safe to ignore, another request might have created the user in the meantime
                    }
                }
            }
        }

        await _next(context);
    }
}
