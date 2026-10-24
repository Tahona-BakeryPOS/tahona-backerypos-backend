namespace BakeryPOS.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid UserId { get; }
    Guid OrganizationId { get; }
    Guid BranchId { get; }
    string FullName { get; }
    IReadOnlyList<string> Permissions { get; }
    bool HasPermission(string permissionCode);
}
