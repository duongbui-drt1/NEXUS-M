using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using NexusM.SharedKernel.Context;

namespace NexusM.BuildingBlocks.Web;

/// <summary>
/// Enforces Idempotency-Key semantics on state-changing POST/PUT/PATCH requests (API-CONVENTIONS.md).
/// Replays identical responses with X-Idempotent-Replayed: true; rejects mismatched payloads with 409.
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next, IMemoryCache cache)
{
    private sealed record CachedIdempotentResponse(string BodyHash, int StatusCode, string ContentType, byte[] BodyBytes);

    public async Task InvokeAsync(HttpContext context, IRequestContext requestContext)
    {
        var method = context.Request.Method;
        if (!HttpMethods.IsPost(method) && !HttpMethods.IsPut(method) && !HttpMethods.IsPatch(method))
        {
            await next(context);
            return;
        }

        var idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            await next(context);
            return;
        }

        if (idempotencyKey.Length > 128)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad Request",
                Detail = "Idempotency-Key must not exceed 128 characters.",
                Extensions = { ["errorCode"] = "IDEMPOTENCY_KEY_TOO_LONG", ["correlationId"] = requestContext.CorrelationId }
            });
            return;
        }

        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{method}:{context.Request.Path}:{rawBody}")));
        var tenantPart = requestContext.TenantId?.ToString("N") ?? "anon";
        var cacheKey = $"nexus-m:{tenantPart}:idemp:{idempotencyKey.Trim()}";

        if (cache.TryGetValue<CachedIdempotentResponse>(cacheKey, out var existing) && existing is not null)
        {
            if (!string.Equals(existing.BodyHash, bodyHash, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Type = "https://errors.nexus.internal/IDEMPOTENCY_PAYLOAD_MISMATCH",
                    Title = "Conflict",
                    Status = StatusCodes.Status409Conflict,
                    Detail = "Idempotency-Key was already used with a different request payload.",
                    Extensions = { ["errorCode"] = "IDEMPOTENCY_PAYLOAD_MISMATCH", ["correlationId"] = requestContext.CorrelationId }
                });
                return;
            }

            context.Response.StatusCode = existing.StatusCode;
            context.Response.ContentType = existing.ContentType;
            context.Response.Headers["X-Idempotent-Replayed"] = "true";
            await context.Response.Body.WriteAsync(existing.BodyBytes);
            return;
        }

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);
            var bytes = buffer.ToArray();

            if (context.Response.StatusCode is >= 200 and < 300)
            {
                cache.Set(
                    cacheKey,
                    new CachedIdempotentResponse(bodyHash, context.Response.StatusCode, context.Response.ContentType ?? "application/json", bytes),
                    TimeSpan.FromHours(24));
            }

            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }
}
