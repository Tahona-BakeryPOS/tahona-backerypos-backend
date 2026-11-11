using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Application.Identity;
using Tahona.BakeryPOS.Domain.Exceptions;
using Tahona.BakeryPOS.Domain.Identity;

namespace Tahona.BakeryPOS.UnitTests.Identity;

public class IdentityDomainTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Fifth_wrong_pin_locks_the_membership_for_five_minutes()
    {
        var membership = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), "hash");

        for (var i = 1; i < LockoutPolicy.MaxFailedAttempts; i++)
            Assert.False(membership.RegisterFailedPin(Now));
        Assert.True(membership.RegisterFailedPin(Now));

        Assert.True(membership.IsPinLocked(Now.AddMinutes(4)));
        Assert.False(membership.IsPinLocked(Now.AddMinutes(5)));
    }

    [Fact]
    public void Successful_pin_resets_the_counter_and_new_pin_unlocks()
    {
        var membership = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), "hash");
        membership.RegisterFailedPin(Now);
        membership.RegisterSuccessfulPin();
        for (var i = 1; i < LockoutPolicy.MaxFailedAttempts; i++)
            Assert.False(membership.RegisterFailedPin(Now));

        membership.RegisterFailedPin(Now);
        membership.SetPin("otro");
        Assert.False(membership.IsPinLocked(Now));
    }

    [Fact]
    public void Password_lockout_follows_the_same_policy()
    {
        var user = User.Create("Ana", "ana@x.co", "hash");
        for (var i = 1; i < LockoutPolicy.MaxFailedAttempts; i++)
            user.RegisterFailedLogin(Now);
        Assert.True(user.RegisterFailedLogin(Now));
        Assert.True(user.IsLockedOut(Now));
    }

    [Fact]
    public void New_user_with_email_must_change_the_one_time_password()
    {
        var user = User.Create(" Ana ", " Ana@X.co ", "hash");
        Assert.Equal("ana@x.co", user.Email);
        Assert.True(user.MustChangePassword);

        user.ChangePassword("nuevo");
        Assert.False(user.MustChangePassword);

        var cashierOnly = User.Create("Luis", null, null);
        Assert.False(cashierOnly.CanUseBackoffice);
        Assert.Throws<DomainException>(() => User.Create("Sin clave", "x@y.co", null));
    }

    [Fact]
    public void Assignment_validity_and_branch_coverage()
    {
        var branch = Guid.NewGuid();
        var everywhere = RoleAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Now, null);
        var onlyOne = RoleAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), branch, Now, Now.AddDays(1));

        Assert.True(everywhere.Covers(Guid.NewGuid()));
        Assert.True(onlyOne.Covers(branch));
        Assert.False(onlyOne.Covers(Guid.NewGuid()));
        Assert.False(onlyOne.IsValidAt(Now.AddMinutes(-1)));
        Assert.False(onlyOne.IsValidAt(Now.AddDays(1)));

        everywhere.End(Now.AddHours(1));
        Assert.False(everywhere.IsValidAt(Now.AddHours(1)));
        Assert.Throws<DomainException>(() => RoleAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Now, Now));
    }

    [Fact]
    public void Pairing_code_is_single_use_and_expires()
    {
        var code = DevicePairingCode.Create(Guid.NewGuid(), Guid.NewGuid(), "hash", Now);
        Assert.False(code.IsUsable(Now + DevicePairingCode.Lifetime));

        code.Consume(Guid.NewGuid(), Now);
        Assert.Throws<DomainException>(() => code.Consume(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Owner_role_has_every_company_permission_but_not_platform_admin()
    {
        var owner = DefaultRoles.All.Single(r => r.Code == SystemRoles.Owner).Permissions;
        Assert.DoesNotContain(Permissions.PlatformAdmin, owner);
        Assert.Equal(Permissions.All.Count - 1, owner.Count);
        Assert.All(DefaultRoles.All.SelectMany(r => r.Permissions), p => Assert.Contains(Permissions.All, d => d.Code == p));
    }

    [Fact]
    public void Secret_tokens_are_random_and_pairing_codes_are_readable()
    {
        Assert.NotEqual(SecretTokens.NewToken(), SecretTokens.NewToken());
        var code = SecretTokens.NewPairingCode();
        Assert.Equal(8, code.Length);
        Assert.DoesNotContain(code, c => "01OIL".Contains(c));
        Assert.Equal(code, SecretTokens.NormalizePairingCode($" {code[..4].ToLowerInvariant()}-{code[4..]} "));
    }
}
