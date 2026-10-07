using System.Text;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Api.Auth;

/// <summary>
/// Creates a user from the command line (e.g. the first administrator; later users can be added in Settings > User roles):
///   dotnet run --project src/api/LT.ODM.Api -- create-user --username jdoe --email jdoe@company.com --name "Jane Doe" --roles Admin [--group FTY --location F001] [--must-change]
/// The password is typed twice without echo and checked against the password policy.
/// </summary>
public static class CreateUserCommand
{
    public static bool IsRequested(string[] args) => args.Length > 0 && args[0] == "create-user";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var options = ParseOptions(args);
        if (!options.TryGetValue("username", out var userName) || !options.TryGetValue("email", out var email))
        {
            Console.Error.WriteLine("Usage: create-user --username <name> --email <email> [--name \"Display Name\"] [--roles Admin,Viewer] [--group FTY] [--location F001] [--must-change]");
            return 1;
        }
        var displayName = options.GetValueOrDefault("name", userName);
        var roles = options.GetValueOrDefault("roles", "Viewer").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        using var scope = services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var repository = scope.ServiceProvider.GetRequiredService<IAuthRepository>();

        string password;
        while (true)
        {
            password = ReadSecret("Password: ");
            var errors = auth.Policy.Validate(password, userName, email, displayName);
            if (errors.Count == 0)
            {
                if (ReadSecret("Confirm password: ") == password) break;
                Console.Error.WriteLine("Passwords do not match.");
                continue;
            }
            foreach (var e in errors) Console.Error.WriteLine($"  - {e}");
        }

        try
        {
            var userId = await repository.CreateUserAsync(userName, email, displayName, hasher.Hash(password), options.ContainsKey("must-change"), roles,
                options.GetValueOrDefault("group"), options.GetValueOrDefault("location"));
            Console.WriteLine($"Created user {userName} (id {userId}) with roles: {string.Join(", ", roles)}.");
            return 0;
        }
        catch (SqlException ex)
        {
            Console.Error.WriteLine($"Could not create the user: {ex.Message}");
            return 1;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            var key = args[i][2..];
            var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            result[key] = hasValue ? args[++i] : "true";
        }
        return result;
    }

    private static string ReadSecret(string prompt)
    {
        Console.Write(prompt);
        var sb = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
            if (!char.IsControl(key.KeyChar)) sb.Append(key.KeyChar);
        }
        Console.WriteLine();
        return sb.ToString();
    }
}
