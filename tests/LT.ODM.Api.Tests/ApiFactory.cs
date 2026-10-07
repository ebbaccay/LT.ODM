using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Ai;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.CostOptimization;
using LT.ODM.Application.MarketTrends;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LT.ODM.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public FakeAuthRepository Users { get; } = new();
    public FakeEmailQueue Emails { get; } = new();
    public FakeMenuRepository Menu { get; } = new();
    public FakeAccessRepository Access { get; } = new();
    public FakeReferenceDataRepository RefData { get; } = new();
    public FakeConceptDraftAi ConceptAi { get; } = new();
    public FakeCostSuggestionAi CostAi { get; } = new();
    public FakeTrendAnalysisAi TrendAi { get; } = new();
    public FakeStyleImportRepository StyleImports { get; } = new();
    public FakeStyleRepository Styles { get; } = new();
    public FakeAiJsonClient TextAi { get; } = new();
    public FakeAiImageClient ImageAi { get; } = new();
    public FakeStyleAiRepository StyleAi { get; } = new();
    public FakeMaterialRepository Materials { get; } = new();
    public FakeAiSettingsRepository AiSettings { get; } = new();
    /// <summary>Uploaded images go to a temp folder that is deleted with the factory.</summary>
    public string FilesRoot { get; } = Path.Combine(Path.GetTempPath(), "ltodm-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Unreachable server with a short timeout: tests must not need a real database.
        builder.UseSetting("ConnectionStrings:StyleLibrary",
            "Server=127.0.0.1,1;Database=x;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=2");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-test-signing-key-test-signing-key");
        builder.UseSetting("Auth:PublicBaseUrl", "https://ltodm.test");
        builder.UseSetting("RateLimiting:AuthPermitPerMinute", "1000");
        builder.UseSetting("AgGrid:LicenseKey", "test-key");
        builder.UseSetting("Files:Root", FilesRoot);
        builder.UseSetting("DataProtection:KeysFolder", Path.Combine(FilesRoot, "keys"));
        builder.UseSetting("RateLimiting:AiPermitPerMinute", "1000");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuthRepository>();
            services.AddSingleton<IAuthRepository>(Users);
            services.RemoveAll<IEmailQueue>();
            services.AddSingleton<IEmailQueue>(Emails);
            services.RemoveAll<IMenuRepository>();
            services.AddSingleton<IMenuRepository>(Menu);
            services.RemoveAll<IAccessRepository>();
            services.AddSingleton<IAccessRepository>(Access);
            services.RemoveAll<IReferenceDataRepository>();
            services.AddSingleton<IReferenceDataRepository>(RefData);
            services.RemoveAll<IConceptDraftAi>();
            services.AddSingleton<IConceptDraftAi>(ConceptAi);
            services.RemoveAll<ICostSuggestionAi>();
            services.AddSingleton<ICostSuggestionAi>(CostAi);
            services.RemoveAll<ITrendAnalysisAi>();
            services.AddSingleton<ITrendAnalysisAi>(TrendAi);
            services.RemoveAll<IStyleImportRepository>();
            services.AddSingleton<IStyleImportRepository>(StyleImports);
            services.RemoveAll<IStyleRepository>();
            services.AddSingleton<IStyleRepository>(Styles);
            services.RemoveAll<IAiJsonClient>();
            services.AddSingleton<IAiJsonClient>(TextAi);
            services.RemoveAll<IAiImageClient>();
            services.AddSingleton<IAiImageClient>(ImageAi);
            services.RemoveAll<IStyleAiRepository>();
            services.AddSingleton<IStyleAiRepository>(StyleAi);
            services.RemoveAll<IMaterialRepository>();
            services.AddSingleton<IMaterialRepository>(Materials);
            services.RemoveAll<IAiSettingsRepository>();
            services.AddSingleton<IAiSettingsRepository>(AiSettings);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { if (Directory.Exists(FilesRoot)) Directory.Delete(FilesRoot, recursive: true); } catch (IOException) { }
    }

    /// <summary>HTTPS client so the Secure refresh cookie is stored and sent back.</summary>
    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
}
