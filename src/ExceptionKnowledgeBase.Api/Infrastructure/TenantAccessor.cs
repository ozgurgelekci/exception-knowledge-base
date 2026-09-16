using Microsoft.AspNetCore.Http;

namespace ExceptionKnowledgeBase.Api.Infrastructure;

// Section 47: read tenant from header, fall back to default.
public static class TenantAccessor
{
    public const string HeaderName = "X-Tenant-Id";
    public const string DefaultTenant = "default";

    public static string ResolveTenantId(this HttpContext ctx)
    {
        if (ctx.Request.Headers.TryGetValue(HeaderName, out var value))
        {
            var raw = value.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
                return raw.Trim();
        }
        return DefaultTenant;
    }
}
