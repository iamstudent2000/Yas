using Xunit;
using YasPortal.Application.Authorization;
using YasPortal.Domain.Organization;
using YasPortal.Domain.Security;

namespace YasPortal.Tests;

public class PasswordPolicyTests
{
    private static PasswordPolicy Make(int min = 8, int max = 128, bool upper = false, bool lower = false, bool digit = false,
        bool symbol = false, bool username = false, bool common = false)
    {
        var p = PasswordPolicy.CreateDefault();
        p.Update(min, max, upper, lower, digit, symbol, username, common, true);
        return p;
    }

    [Fact]
    public void Default_policy_needs_eight_characters_and_a_digit()
    {
        var p = PasswordPolicy.CreateDefault();
        Assert.Equal(8, p.MinLength);
        Assert.NotEmpty(p.Validate("abcdefgh"));
        Assert.NotEmpty(p.Validate("a1"));
        Assert.Empty(p.Validate("abcdefg1"));
    }

    [Fact]
    public void Length_limits_are_enforced()
    {
        var p = Make(min: 6, max: 10);
        Assert.NotEmpty(p.Validate("12345"));
        Assert.Empty(p.Validate("123456"));
        Assert.Empty(p.Validate("1234567890"));
        Assert.NotEmpty(p.Validate("12345678901"));
    }

    [Fact]
    public void Character_classes_are_required_only_when_enabled()
    {
        var p = Make(min: 4, upper: true, lower: true, digit: true, symbol: true);
        Assert.Equal(3, p.Validate("aaaa").Count); // no upper, no digit, no symbol
        Assert.Empty(p.Validate("aB3!"));
        Assert.Single(p.Validate("aB3x"));
        Assert.Empty(Make(min: 4).Validate("aaaa"));
    }

    [Fact]
    public void Username_and_common_passwords_can_be_rejected()
    {
        var p = Make(min: 4, username: true, common: true);
        Assert.NotEmpty(p.Validate("xxAliceXX", "alice"));
        Assert.Empty(p.Validate("xxBobXX", "alice"));
        Assert.NotEmpty(p.Validate("Password123"));
        Assert.Empty(Make(min: 4).Validate("password123"));
    }

    [Fact]
    public void Update_rejects_impossible_limits()
    {
        var p = PasswordPolicy.CreateDefault();
        Assert.Throws<ArgumentOutOfRangeException>(() => p.Update(2, 20, false, false, false, false, false, false, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.Update(10, 9, false, false, false, false, false, false, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.Update(8, 500, false, false, false, false, false, false, false));
    }

    [Fact]
    public void Describe_lists_the_active_rules()
    {
        var lines = Make(min: 10, digit: true, symbol: true).Describe();
        Assert.Contains(lines, x => x.Contains("10"));
        Assert.Contains(lines, x => x.Contains("رقم"));
        Assert.Contains(lines, x => x.Contains("نماد"));
        Assert.DoesNotContain(lines, x => x.Contains("حرف بزرگ"));
    }

    [Fact]
    public void Employee_avatar_can_be_set_and_cleared()
    {
        var e = new Employee("E1", "u1", "Name", Guid.NewGuid());
        Assert.Null(e.AvatarKey);
        e.SetAvatar(" rocket ");
        Assert.Equal("rocket", e.AvatarKey);
        e.SetAvatar(null);
        Assert.Null(e.AvatarKey);
        Assert.Throws<ArgumentException>(() => e.SetAvatar(new string('x', 41)));
    }

    [Fact]
    public void The_password_permissions_are_in_the_catalog_once_and_manage_implies_view()
    {
        foreach (var code in new[] { Perm.AdminPasswordPolicyView, Perm.AdminPasswordPolicyManage, Perm.AdminUsersPasswordSet })
            Assert.Single(PermissionCatalog.All, x => x.Code == code);
        Assert.Equal(Perm.AdminPasswordPolicyManage, Perm.ManageFor(Perm.AdminPasswordPolicyView));
        Assert.Equal(PermissionCatalog.All.Count, PermissionCatalog.Codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
