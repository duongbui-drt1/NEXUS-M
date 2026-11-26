using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Results;

namespace NexusM.BuildingBlocks.Web;

public static class ApiResultMapper
{
    public static IActionResult ToActionResult<T>(
        this Result<T> result,
        HttpContext httpContext,
        int successStatusCode = StatusCodes.Status200OK,
        DateTimeOffset? asOfUtc = null)
    {
        var correlationId = httpContext.RequestServices.GetService(typeof(IRequestContext)) is IRequestContext ctx
            ? ctx.CorrelationId
            : httpContext.TraceIdentifier;

        if (result.IsSuccess)
        {
            var payload = new ApiResponse<T>(true, result.Value, new ApiMeta(correlationId, asOfUtc));
            return new ObjectResult(payload) { StatusCode = successStatusCode };
        }

        return ToProblemResult(result.Error!, httpContext, correlationId);
    }

    public static IActionResult ToProblemResult(Error error, HttpContext httpContext, string? correlationId = null)
    {
        correlationId ??= httpContext.RequestServices.GetService(typeof(IRequestContext)) is IRequestContext ctx
            ? ctx.CorrelationId
            : httpContext.TraceIdentifier;

        var statusCode = (int)error.Type;
        var problem = new ProblemDetails
        {
            Type = $"https://errors.nexus.internal/{error.Code}",
            Title = error.Type.ToString(),
            Status = statusCode,
            Detail = error.Message
        };
        problem.Extensions["errorCode"] = error.Code;
        problem.Extensions["correlationId"] = correlationId;

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }
}
