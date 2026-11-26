using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NexusM.SharedKernel.Context;

namespace NexusM.BuildingBlocks.Web;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var correlationId = httpContext.RequestServices.GetService(typeof(IRequestContext)) is IRequestContext ctx
            ? ctx.CorrelationId
            : httpContext.TraceIdentifier;

        logger.LogError(exception, "Unhandled exception on {Method} {Path} (CorrelationId={CorrelationId})",
            httpContext.Request.Method, httpContext.Request.Path, correlationId);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Type = "https://errors.nexus.internal/INTERNAL_ERROR",
            Title = "Internal Server Error",
            Status = StatusCodes.Status500InternalServerError,
            Detail = "An unexpected error occurred. Please reference the correlationId when contacting support.",
            Extensions =
            {
                ["errorCode"] = "INTERNAL_ERROR",
                ["correlationId"] = correlationId
            }
        }, cancellationToken);

        return true;
    }
}
