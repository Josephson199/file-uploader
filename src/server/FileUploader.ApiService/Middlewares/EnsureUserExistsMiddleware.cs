using FileUploader.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FileUploader.ApiService.Middlewares;

public class EnsureUserExistsMiddleware
{
    private readonly RequestDelegate _next;

    public EnsureUserExistsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IServiceProvider services)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var subject = context.User.FindFirst("sub")?.Value;

            if (!string.IsNullOrEmpty(subject))
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var exists = await db.Users.AnyAsync(u => u.Sub == subject);

                if (!exists)
                {
                    try 
                    {
                        db.Users.Add(new User
                        {
                            Sub = subject,
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
