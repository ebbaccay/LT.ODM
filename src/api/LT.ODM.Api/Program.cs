using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using LT.ODM.Api;
using LT.ODM.Api.Auth;
using LT.ODM.Api.Controllers;
using LT.ODM.Api.Health;
using LT.ODM.Api.Hubs;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Admin;
using LT.ODM.Application.Ai;
using LT.ODM.Application.Auth;
using LT.ODM.Infrastructure;
using LT.ODM.Infrastructure.Email;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Allowlist for the TMS compatibility hub (ported TMS modules), see tms-procedures.json.
// Lowest priority, so appsettings, user secrets and environment variables can still override it.
builder.Configuration.Sources.Insert(0, new JsonConfigurationSource { Path = "tms-procedures.json", Optional = false });

var connectionString = builder.Configuration.GetConnectionString("StyleLibrary");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'ConnectionStrings:StyleLibrary' is not configured. " +
        "In development run: dotnet user-secrets set \"ConnectionStrings:StyleLibrary\" \"...\" --project src/api/LT.ODM.Api. " +
        "In production set the environment variable ConnectionStrings__StyleLibrary.");
}

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (Encoding.UTF8.GetByteCount(jwtOptions.SigningKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:SigningKey is missing or shorter than 32 bytes. " +
        "In development run: dotnet user-secrets set \"Jwt:SigningKey\" \"<64 random characters>\" --project src/api/LT.ODM.Api. " +
        "In production set the environment variable Jwt__SigningKey.");
}

if (string.IsNullOrWhiteSpace(builder.Configuration[$"{AuthOptions.SectionName}:{nameof(AuthOptions.PublicBaseUrl)}"]))
{
    throw new InvalidOperationException(
        "Auth:PublicBaseUrl is not configured (the app URL used in password reset emails). " +
        "Set it in appsettings.{Environment}.json or the environment variable Auth__PublicBaseUrl.");
}

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddTmsProcedures(builder.Configuration);
builder.Services.AddConceptStudio(builder.Configuration, Path.Combine(builder.Environment.ContentRootPath, "App_Data", "uploads"));
builder.Services.AddAiStudio(builder.Configuration);
// Settings > Translations: corrections kept in App_Data/i18n and layered on the deployed wwwroot/assets/i18n files.
builder.Services.AddTranslations(builder.Configuration, builder.Environment.ContentRootPath,
    builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"));

// API keys typed in Settings > AI connections are encrypted with Data Protection. The key ring is kept outside the web
// root (DataProtection:KeysFolder, default App_Data/keys) and, on Windows, encrypted with DPAPI for the machine.
// Back the folder up with the database: without it the saved API keys cannot be read and must be entered again.
var keysFolder = builder.Configuration["DataProtection:KeysFolder"] is { Length: > 0 } configuredKeys
    ? configuredKeys
    : Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("LT.ODM").PersistKeysToFileSystem(new DirectoryInfo(keysFolder));
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
builder.Services.AddSingleton<IAiSecretProtector, DataProtectionAiSecretProtector>();

// Authentication: short-lived JWT access tokens + rotating refresh tokens (HttpOnly cookie).
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.PostConfigure<EmailOptions>(o => o.LogBodyWhenNotConfigured = builder.Environment.IsDevelopment());
builder.Services.AddSingleton<IAccessTokenService, JwtAccessTokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UserInviteService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = jwtOptions.CreateKey(),
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "preferred_username",
            RoleClaimType = "role",
        };
        // Browsers cannot send headers on WebSocket connections: SignalR passes the token in the query string.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = token;
                return Task.CompletedTask;
            },
        };
    });

// Secure by default: every endpoint needs a signed-in user unless marked [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSignalR()
    .AddHubOptions<TmsProcedureHub>(o =>
    {
        // TMS screens send several calls at once and bulk rows in one message.
        o.MaximumParallelInvocationsPerClient = 16;
        o.MaximumReceiveMessageSize = 1024 * 1024;
    });
builder.Services.AddProblemDetails();

builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("sqlserver");

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularDev", policy => policy
        .WithOrigins("http://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Default policy: 100 requests per minute per client IP.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    // Sign-in and password endpoints: 10 requests per minute per client IP by default (slows password guessing).
    var authPermitPerMinute = builder.Configuration.GetValue("RateLimiting:AuthPermitPerMinute", 10);
    options.AddPolicy(RateLimitPolicies.Auth, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    // AI calls cost money and take seconds: 10 per minute per signed-in user by default.
    var aiPermitPerMinute = builder.Configuration.GetValue("RateLimiting:AiPermitPerMinute", 10);
    options.AddPolicy(RateLimitPolicies.Ai, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = aiPermitPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

if (CreateUserCommand.IsRequested(args))
{
    return await CreateUserCommand.RunAsync(app.Services, args);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "LT ODM API v1"));
    app.UseDeveloperExceptionPage();
}
else
{
    // ProblemDetails without exception details outside Development.
    app.UseExceptionHandler();
    app.UseHsts();
}
app.UseStatusCodePages();
app.UseHttpsRedirection();

// The web app (Angular build in wwwroot) is served by the API, so app and API share one address (one IIS site).
// Before the no-store header below: hashed bundles are cached for a year; index.html, the service worker files and
// the translations are re-checked on every load so a new release reaches users straight away.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = WebAppFiles.ContentTypes,
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = WebAppFiles.CacheControl(ctx.File.Name),
});

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        // The app page (index.html, served by the fallback below) sets its own no-cache.
        if (string.IsNullOrEmpty(context.Response.Headers.CacheControl)) context.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    });
    await next();
});

// Explicit, so static files above are served before any endpoint (incl. the app-route fallback) is matched.
app.UseRouting();

if (app.Environment.IsDevelopment())
{
    app.UseCors("AngularDev");
}

// After authentication so per-user policies (AI) can see who is calling; per-IP policies are unaffected.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() })
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}).AllowAnonymous();

app.MapHub<NotificationsHub>("/hubs/notifications");
app.MapHub<TmsProcedureHub>("/hubs/sp");

// Unknown API and hub addresses stay 404 (never the app page); any other address without a file extension is an
// app route (/styles/12, /settings/import) and gets index.html, so reloads and shared links work.
app.MapFallback("api/{**rest}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found."));
app.MapFallback("hubs/{**rest}", () => Results.NotFound());
// A file that is not in the build (an old bundle name after a release): 404 without asking for sign-in.
app.MapFallback("{*path:file}", () => Results.NotFound()).AllowAnonymous();
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
}).AllowAnonymous();

await app.RunAsync();
return 0;

public partial class Program;
