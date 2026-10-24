using BakeryPOS.Domain.Entities;

namespace BakeryPOS.Application.Common.Interfaces;

public interface IJwtTokenService
{
    string GenerateToken(AppUser user, Guid organizationId, Guid branchId, IEnumerable<string> permissions);
    string GenerateRefreshToken();
}
