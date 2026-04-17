using AsaServerManager.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AsaServerManager.Web.Infrastructure.Auth;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireApiKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        AuthService authService = context.HttpContext.RequestServices.GetRequiredService<AuthService>();
        string? apiKey = context.HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
        bool isValid = await authService.IsApiKeyValidAsync(apiKey, context.HttpContext.RequestAborted);

        if (isValid)
        {
            return;
        }

        context.Result = new UnauthorizedObjectResult(new
        {
            success = false,
            message = "Invalid API key."
        });
    }
}
