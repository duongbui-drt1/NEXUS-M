using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NexusM.BuildingBlocks.NexusCore;
using NexusM.BuildingBlocks.Web;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Results;
using NexusM.SharedKernel.Security;

namespace NexusM.BuildingBlocks.Security;

public sealed class CorrelationAndRequestContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IRequestContext requestContext)
    {
        var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.CreateVersion7().ToString("D");
        }

        requestContext.SetCorrelationId(correlationId);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Correlation-Id"] = requestContext.CorrelationId;
            return Task.CompletedTask;
        });

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
            var email = context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.FindFirstValue("email") ?? string.Empty;
            var tidClaim = context.User.FindFirstValue("tid");
            var scopeClaim = context.User.FindFirstValue("scope");
            var isPlatform = string.Equals(scopeClaim, "platform", StringComparison.OrdinalIgnoreCase);

            Guid? tid = Guid.TryParse(tidClaim, out var parsedTid) ? parsedTid : null;

            // Strict anti-spoofing check: X-Tenant-Id header MUST NOT conflict with JWT tid claim (ADR-001)
            var headerTidRaw = context.Request.Headers["X-Tenant-Id"].FirstOrDefault()
                               ?? context.Request.Headers["Tenant-ID"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerTidRaw))
            {
                if (!Guid.TryParse(headerTidRaw, out var headerTid) || (tid.HasValue && headerTid != tid.Value))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/problem+json";
                    await context.Response.WriteAsJsonAsync(new ProblemDetails
                    {
                        Type = "https://errors.nexus.internal/TENANT_SPOOFING_DETECTED",
                        Title = "Forbidden",
                        Status = StatusCodes.Status403Forbidden,
                        Detail = "Request tenant header conflicts with verified token tenant context.",
                        Extensions =
                        {
                            ["errorCode"] = "TENANT_SPOOFING_DETECTED",
                            ["correlationId"] = requestContext.CorrelationId
                        }
                    });
                    return;
                }
            }

            if (Guid.TryParse(sub, out var userId))
            {
                var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
                var rawToken = authHeader is not null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? authHeader["Bearer ".Length..].Trim()
                    : null;

                requestContext.SetPrincipal(userId, email, tid, isPlatform, rawToken);
            }
        }

        await next(context);
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireNexusMPermissionAttribute(string permissionCode, string entitlementCode = NexusMPermissions.ApplicationCode)
    : TypeFilterAttribute(typeof(NexusMAuthorizationFilter))
{
    public string PermissionCode { get; } = permissionCode;
    public string EntitlementCode { get; } = entitlementCode;
}

public sealed class NexusMAuthorizationFilter(
    IRequestContext requestContext,
    INexusCoreClient nexusCoreClient) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!requestContext.IsAuthenticated || !requestContext.UserId.HasValue || string.IsNullOrWhiteSpace(requestContext.RawBearerToken))
        {
            context.Result = ApiResultMapper.ToProblemResult(
                Error.Unauthorized("AUTH_REQUIRED", "Valid NEXUS C authentication token is required."),
                context.HttpContext);
            return;
        }

        if (!requestContext.TenantId.HasValue)
        {
            context.Result = ApiResultMapper.ToProblemResult(
                Error.Forbidden("TENANT_CONTEXT_REQUIRED", "An active tenant context (tid) is required for NEXUS M."),
                context.HttpContext);
            return;
        }

        var attrs = context.ActionDescriptor.EndpointMetadata
            .OfType<RequireNexusMPermissionAttribute>()
            .ToList();

        var tenantId = requestContext.TenantId.Value;
        var ct = context.HttpContext.RequestAborted;

        // 1. Entitlement Gate (fail-closed)
        var requiredEntitlements = attrs.Select(a => a.EntitlementCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (requiredEntitlements.Count == 0)
        {
            requiredEntitlements.Add(NexusMPermissions.ApplicationCode);
        }

        foreach (var featureCode in requiredEntitlements)
        {
            var entResult = await nexusCoreClient.EvaluateEntitlementAsync(
                tenantId,
                featureCode,
                requestContext.RawBearerToken,
                requestContext.CorrelationId,
                ct);

            if (entResult.IsFailure)
            {
                context.Result = ApiResultMapper.ToProblemResult(entResult.Error!, context.HttpContext);
                return;
            }

            if (!entResult.Value.IsEntitled)
            {
                context.Result = ApiResultMapper.ToProblemResult(
                    Error.Forbidden("ENTITLEMENT_DENIED", $"Tenant is not entitled to '{featureCode}' ({entResult.Value.Reason})."),
                    context.HttpContext);
                return;
            }
        }

        // 2. Authorization & Org-Scope Gate (fail-closed)
        var authzResult = await nexusCoreClient.GetUserAuthorizationSnapshotAsync(
            tenantId,
            requestContext.UserId.Value,
            requestContext.RawBearerToken,
            requestContext.CorrelationId,
            ct);

        if (authzResult.IsFailure)
        {
            context.Result = ApiResultMapper.ToProblemResult(authzResult.Error!, context.HttpContext);
            return;
        }

        requestContext.SetAuthorizationState(authzResult.Value.Permissions, authzResult.Value.OrgScope);

        foreach (var attr in attrs)
        {
            if (!requestContext.Permissions.Contains(attr.PermissionCode))
            {
                context.Result = ApiResultMapper.ToProblemResult(
                    Error.Forbidden("PERMISSION_DENIED", "You do not have permission to perform this action."),
                    context.HttpContext);
                return;
            }
        }

        await next();
    }
}
