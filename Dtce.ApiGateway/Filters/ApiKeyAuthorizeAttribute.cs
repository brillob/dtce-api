using Dtce.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dtce.ApiGateway.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ApiKeyAuthorizeAttribute : Attribute, IAsyncActionFilter
{
    private const string ApiKeyHeaderName = "X-API-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<ApiKeyAuthorizeAttribute>>();
        var userService = context.HttpContext.RequestServices.GetService<IUserService>();
        var configuration = context.HttpContext.RequestServices.GetService<IConfiguration>();

        if (userService == null)
        {
            logger.LogError("IUserService is not registered in the service collection.");
            context.Result = new StatusCodeResult(StatusCodes.Status500InternalServerError);
            return;
        }

        // In development mode with local storage, allow requests without API key for testing
        var platformMode = configuration?["Platform:Mode"] ?? "Prod";
        var isDevMode = string.Equals(platformMode, "Dev", StringComparison.OrdinalIgnoreCase);
        var isDevelopment = context.HttpContext.RequestServices.GetService<IWebHostEnvironment>()?.IsDevelopment() ?? false;

        if (isDevMode || isDevelopment)
        {
            // In dev mode, allow requests without API key or with any API key
            if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var potentialApiKey) ||
                string.IsNullOrWhiteSpace(potentialApiKey))
            {
                logger.LogInformation("Dev mode: Allowing request without API key");
                await next();
                return;
            }

            // If API key is provided, try to validate it, but don't fail if it's invalid in dev mode
            if (await userService.ValidateApiKeyAsync(potentialApiKey!, context.HttpContext.RequestAborted))
            {
                logger.LogInformation("Dev mode: Valid API key provided");
            }
            else
            {
                logger.LogInformation("Dev mode: Invalid API key provided, but allowing request");
            }

            await next();
            return;
        }

        // Production mode: require valid API key
        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKey) ||
            string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("API request missing {Header}", ApiKeyHeaderName);
            context.Result = new UnauthorizedObjectResult(new { error = "Missing API key" });
            return;
        }

        if (!await userService.ValidateApiKeyAsync(apiKey!, context.HttpContext.RequestAborted))
        {
            logger.LogWarning("Invalid API key supplied.");
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid API key" });
            return;
        }

        await next();
    }
}


