using System.Text;

namespace LT.ODM.Application.Auth;

/// <summary>
/// Strong password rules. The Angular strength meter mirrors the first rules for instant feedback;
/// this class is the source of truth (common passwords, patterns and personal info are only checked here).
/// </summary>
public sealed class PasswordPolicy(PasswordPolicyOptions options)
{
    public PasswordPolicyOptions Options => options;

    /// <summary>Returns the rules the password breaks; empty when it is acceptable.</summary>
    public IReadOnlyList<string> Validate(string? password, params string?[] personalInfo)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(password))
        {
            errors.Add("Password is required.");
            return errors;
        }

        if (password.Length < options.MinLength)
            errors.Add($"Use at least {options.MinLength} characters.");
        if (password.Length > options.MaxLength)
            errors.Add($"Use at most {options.MaxLength} characters.");
        if (options.RequireUppercase && !password.Any(char.IsUpper))
            errors.Add("Add an uppercase letter.");
        if (options.RequireLowercase && !password.Any(char.IsLower))
            errors.Add("Add a lowercase letter.");
        if (options.RequireDigit && !password.Any(char.IsDigit))
            errors.Add("Add a number.");
        if (options.RequireSymbol && password.All(char.IsLetterOrDigit))
            errors.Add("Add a symbol such as ! @ # $ %.");
        if (password.Any(char.IsControl))
            errors.Add("Remove control characters.");

        if (HasRepeatedRun(password, 4))
            errors.Add("Avoid repeating the same character 4 or more times.");
        if (HasSequentialRun(password, 4))
            errors.Add("Avoid sequences such as 1234, abcd or qwer.");
        if (CommonPasswords.IsCommon(password))
            errors.Add("This password is too common. Choose something less predictable.");
        if (ContainsPersonalInfo(password, personalInfo))
            errors.Add("Don't use your name, user name or email in your password.");

        return errors;
    }

    private static bool HasRepeatedRun(string value, int length)
    {
        var run = 1;
        for (var i = 1; i < value.Length; i++)
        {
            run = char.ToLowerInvariant(value[i]) == char.ToLowerInvariant(value[i - 1]) ? run + 1 : 1;
            if (run >= length) return true;
        }
        return false;
    }

    private const string KeyboardRows = "qwertyuiopasdfghjklzxcvbnm";

    private static bool HasSequentialRun(string value, int length)
    {
        var lower = value.ToLowerInvariant();
        int up = 1, down = 1;
        for (var i = 1; i < lower.Length; i++)
        {
            var bothAlnum = char.IsLetterOrDigit(lower[i]) && char.IsLetterOrDigit(lower[i - 1]);
            up = bothAlnum && lower[i] == lower[i - 1] + 1 ? up + 1 : 1;
            down = bothAlnum && lower[i] == lower[i - 1] - 1 ? down + 1 : 1;
            if (up >= length || down >= length) return true;
        }

        for (var i = 0; i + length <= lower.Length; i++)
        {
            var chunk = lower.Substring(i, length);
            if (KeyboardRows.Contains(chunk, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static bool ContainsPersonalInfo(string password, string?[] personalInfo)
    {
        var lower = password.ToLowerInvariant();
        foreach (var info in personalInfo)
        {
            if (string.IsNullOrWhiteSpace(info)) continue;
            // Check the whole value and its parts (email local part, first/last name, user.name pieces).
            var parts = info.ToLowerInvariant()
                .Split(['@', '.', '_', '-', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Append(info.ToLowerInvariant().Split('@')[0]);
            if (parts.Any(p => p.Length >= 3 && lower.Contains(p, StringComparison.Ordinal))) return true;
        }
        return false;
    }
}

internal static class CommonPasswords
{
    // Small built-in list of the most common bases. Variants like "P@ssw0rd2026!" are caught by normalising.
    private static readonly HashSet<string> List = new(StringComparer.Ordinal)
    {
        "password", "passwort", "pass", "welcome", "letmein", "admin", "administrator", "login", "user",
        "qwerty", "azerty", "iloveyou", "monkey", "dragon", "sunshine", "princess", "football", "baseball",
        "basketball", "soccer", "master", "shadow", "superman", "batman", "trustno", "starwars", "hello",
        "freedom", "whatever", "changeme", "secret", "default", "guest", "test", "temp", "temporary",
        "summer", "winter", "spring", "autumn", "fall", "january", "february", "march", "april", "may",
        "june", "july", "august", "september", "october", "november", "december",
        "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday",
        "company", "office", "computer", "internet", "server", "database", "system",
        "ltodm", "odm", "lt", "stylelibrary", "style", "library", "garment", "fashion", "apparel", "factory",
        "abc", "abcdef", "asdf", "zxcv", "qazwsx", "hunter", "killer", "pokemon", "matrix", "love", "god",
    };

    /// <summary>
    /// "Summer2026!" -> "summer", "P@ssw0rd123" -> "password", "!Welcome1" -> "welcome".
    /// A password that is only digits/symbols also counts as common.
    /// </summary>
    public static bool IsCommon(string password)
    {
        // 1. drop leading/trailing digits and symbols, 2. undo look-alike substitutions inside the word.
        var core = password.ToLowerInvariant().Trim().Trim(DigitsAndSymbols);
        if (core.Length == 0) return true;
        return List.Contains(core) || List.Contains(UndoSubstitutions(core));
    }

    private static readonly char[] DigitsAndSymbols = "0123456789!@#$%^&*()_+-=[]{};:'\",.<>/?\\|`~ ".ToCharArray();

    private static string UndoSubstitutions(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '@' or '4' => 'a',
                '0' => 'o',
                '1' or '!' => 'i',
                '3' => 'e',
                '$' or '5' => 's',
                '7' => 't',
                _ => c,
            });
        }
        return sb.ToString();
    }
}
