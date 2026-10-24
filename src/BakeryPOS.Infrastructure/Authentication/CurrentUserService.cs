using System.Security.Claims;
using BakeryPOS.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace BakeryPOS.Infrastructure.Authentication;

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid UserId =>
        Guid.TryParse(_httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? _httpContextAccessor.HttpContext?.User.FindFirstValue("sub"), out var id)
            ? id : Guid.Empty;

    public Guid OrganizationId =>
        Guid.TryParse(_httpContextAccessor.HttpContext?.User.FindFirstValue("org_id"), out var id)
            ? id : Guid.Empty;

    public Guid BranchId =>
        Guid.TryParse(_httpContextAccessor.HttpContext?.User.FindFirstValue("branch_id"), out var id)
            ? id : Guid.Empty;

    public string FullName =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue("full_name") ?? string.Empty;

    public IReadOnlyList<string> Permissions =>
        _httpContextAccessor.HttpContext?.User.FindAll("permission")
            .Select(c => c.Value)
            .ToList()
            .AsReadOnly()
        ?? (IReadOnlyList<string>)Array.Empty<string>();

    public bool HasPermission(string permissionCode) =>
        Permissions.Contains(permissionCode, StringComparer.OrdinalIgnoreCase);
}
