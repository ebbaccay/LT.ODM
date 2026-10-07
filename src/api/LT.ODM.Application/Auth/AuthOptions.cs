namespace LT.ODM.Application.Auth;

/// <summary>Bound from the "Auth" configuration section.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Public URL of the Angular app, used to build password reset links (e.g. https://ltodm.company.local).</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;

    public int AccessTokenMinutes { get; set; } = 15;
    /// <summary>Refresh token lifetime with "Keep me signed in".</summary>
    public int PersistentSessionDays { get; set; } = 14;
    /// <summary>Refresh token lifetime without "Keep me signed in".</summary>
    public int SessionHours { get; set; } = 12;

    public int ResetTokenMinutes { get; set; } = 30;
    public int ResetRequestsPerHour { get; set; } = 3;
    /// <summary>Lifetime of the "set your password" link sent to new users and by administrators.</summary>
    public int InviteTokenHours { get; set; } = 72;

    public PasswordPolicyOptions Password { get; set; } = new();
}

public sealed class PasswordPolicyOptions
{
    public int MinLength { get; set; } = 12;
    public int MaxLength { get; set; } = 128;
    public bool RequireUppercase { get; set; } = true;
    public bool RequireLowercase { get; set; } = true;
    public bool RequireDigit { get; set; } = true;
    public bool RequireSymbol { get; set; } = true;
    /// <summary>How many previous passwords (including the current one) cannot be re-used.</summary>
    public int HistoryCount { get; set; } = 5;
}
