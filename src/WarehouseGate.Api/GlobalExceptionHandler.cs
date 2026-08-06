using Microsoft.AspNetCore.Diagnostics;

namespace WarehouseGate.Api;

// Catches anything the per-controller Handle/HandleNoContent helpers don't already map to a
// clean response (KeyNotFoundException/UnauthorizedAccessException/InvalidOperationException) -
// genuinely unexpected exceptions (bugs, DB failures, null refs) land here instead of surfacing
// as an unhandled ASP.NET Core 500 with no structured logging.
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Unhandled exception on {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(new { message = "An unexpected error occurred." }, cancellationToken);

        return true;
    }
}
