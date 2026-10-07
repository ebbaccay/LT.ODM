using LT.ODM.Application.Auth;

namespace LT.ODM.Api.Tests;

public sealed class PasswordPolicyTests
{
    private readonly PasswordPolicy _policy = new(new PasswordPolicyOptions());

    [Theory]
    [InlineData("Sh0rt!pw", "at least 12")]                  // too short
    [InlineData("alllowercase1!x", "uppercase")]
    [InlineData("ALLUPPERCASE1!X", "lowercase")]
    [InlineData("NoDigitsHere!!x", "number")]
    [InlineData("NoSymbols123xyZ", "symbol")]
    [InlineData("Summer2026!!", "too common")]               // season + year
    [InlineData("P@ssw0rd2026!", "too common")]              // look-alike substitutions
    [InlineData("Welcome@12345", "too common")]
    [InlineData("Xk9!aaaa#Lm2q", "repeating")]
    [InlineData("Zq!1234#Pwerx", "sequences")]
    [InlineData("Hy7#qwerTyzz", "sequences")]                // keyboard row
    public void Rejects_weak_passwords(string password, string expectedMessagePart)
    {
        var errors = _policy.Validate(password);
        Assert.Contains(errors, e => e.Contains(expectedMessagePart, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_password_containing_user_name_or_email()
    {
        var errors = _policy.Validate("Mh7!JDoe#Rv92", "jdoe", "jane.doe@company.com", "Jane Doe");
        Assert.Contains(errors, e => e.Contains("user name", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Tr0ub4dor&Horse-Battery")]
    [InlineData("Mh7!Rv92#Kp4w")]
    [InlineData("correct Horse 9 battery!")]
    public void Accepts_strong_passwords(string password)
        => Assert.Empty(_policy.Validate(password, "jdoe", "jane.doe@company.com", "Jane Doe"));
}
