using LT.ODM.Application.Abstractions;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.CostOptimization;
using LT.ODM.Application.Ai;
using LT.ODM.Application.MarketTrends;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using LT.ODM.Application.Translations;
using LT.ODM.Infrastructure.Ai;
using LT.ODM.Infrastructure.Data;
using LT.ODM.Infrastructure.Email;
using LT.ODM.Infrastructure.Files;
using LT.ODM.Infrastructure.Repositories;
using LT.ODM.Infrastructure.Security;
using LT.ODM.Infrastructure.StyleLibrary;
using LT.ODM.Infrastructure.Tms;
using LT.ODM.Infrastructure.Translations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IDbConnectionFactory>(new SqlConnectionFactory(connectionString));
        services.AddScoped<IHealthRepository, HealthRepository>();
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<IMenuRepository, MenuRepository>();
        services.AddScoped<IAccessRepository, AccessRepository>();
        services.AddScoped<IReferenceDataRepository, ReferenceDataRepository>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<IStyleWorkbookReader, StyleWorkbookReader>();
        services.AddScoped<IStyleImportRepository, StyleImportRepository>();
        services.AddScoped<IStyleRepository, StyleRepository>();
        services.AddScoped<IMaterialRepository, MaterialRepository>();

        services.AddSingleton<EmailQueue>();
        services.AddSingleton<IEmailQueue>(sp => sp.GetRequiredService<EmailQueue>());
        services.AddHostedService<EmailSenderService>();
        return services;
    }

    /// <summary>Concept Studio, Cost Optimization and Market Trends: AI through the Text job (Settings > AI connections) and image storage for concept inspiration, SBU products and the Style Library (Files:Root).</summary>
    public static IServiceCollection AddConceptStudio(this IServiceCollection services, IConfiguration configuration, string defaultFilesRoot)
    {
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
        // Concept Studio, Cost Optimization and Market Trends use the Text job (IAiJsonClient, registered in AddAiStudio).
        services.AddScoped<IConceptDraftAi, ConceptDraftAi>();
        services.AddScoped<ICostSuggestionAi, CostSuggestionAi>();
        services.AddScoped<ITrendAnalysisAi, TrendAnalysisAi>();
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        services.PostConfigure<FileStorageOptions>(o => { if (string.IsNullOrWhiteSpace(o.Root)) o.Root = defaultFilesRoot; });
        services.AddSingleton<IConceptImageStore, FileSystemConceptImageStore>();
        services.AddSingleton<IProductImageStore, FileSystemProductImageStore>();
        services.AddSingleton<IStyleImageStore, FileSystemStyleImageStore>();
        return services;
    }

    /// <summary>
    /// AI Studio (smart search, change summary, BOM check, renders). The service per job is set in Settings > AI connections;
    /// jobs not set there use Ai:Provider / Ai:ImageProvider (Gemini or OpenAiCompatible). Needs an IAiSecretProtector (the API adds it).
    /// Call after AddConceptStudio (Gemini key, image store).
    /// </summary>
    public static IServiceCollection AddAiStudio(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<OpenAiCompatibleOptions>(configuration.GetSection(OpenAiCompatibleOptions.SectionName));
        // Each call asks the resolver which service to use: Settings > AI connections, else appsettings.
        services.AddSingleton<IAiConnectionResolver, AiConnectionResolver>();
        services.AddScoped<IAiSettingsRepository, AiSettingsRepository>();
        services.AddHttpClient<IAiJsonClient, RoutedAiJsonClient>();
        services.AddHttpClient<IAiImageClient, RoutedAiImageClient>();
        services.AddHttpClient<AiConnectionTester>();

        services.AddScoped<IStyleAiRepository, StyleAiRepository>();
        services.AddScoped<StyleAiAssistant>();
        services.AddScoped<ImportCodeAdvisor>();
        services.AddScoped<ConceptMatcher>();
        services.AddScoped<IMaterialSpecRepository, MaterialSpecRepository>();
        services.AddScoped<MaterialReader>();
        return services;
    }

    /// <summary>
    /// Settings > Translations: corrections layered on the deployed translation files. Relative folders start at
    /// <paramref name="contentRoot"/>; defaults are the web root's assets/i18n and App_Data/i18n.
    /// </summary>
    public static IServiceCollection AddTranslations(this IServiceCollection services, IConfiguration configuration, string contentRoot, string webRoot)
    {
        services.Configure<TranslationOptions>(configuration.GetSection(TranslationOptions.SectionName));
        services.PostConfigure<TranslationOptions>(o =>
        {
            o.BaseFolder = Path.GetFullPath(string.IsNullOrWhiteSpace(o.BaseFolder) ? Path.Combine(webRoot, "assets", "i18n") : o.BaseFolder, contentRoot);
            o.OverridesFolder = Path.GetFullPath(string.IsNullOrWhiteSpace(o.OverridesFolder) ? Path.Combine("App_Data", "i18n") : o.OverridesFolder, contentRoot);
        });
        services.AddSingleton<ITranslationStore, FileTranslationStore>();
        services.AddSingleton<ITranslationWorkbook, TranslationWorkbook>();
        services.AddScoped<TranslationService>();
        return services;
    }

    /// <summary>TMS compatibility: allowlisted stored procedures for the ported TMS modules.</summary>
    public static IServiceCollection AddTmsProcedures(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(TmsProcedureOptions.SectionName);
        services.Configure<TmsProcedureOptions>(section);

        var name = section[nameof(TmsProcedureOptions.ConnectionStringName)] ?? "StyleLibrary";
        var connectionString = configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Connection string '{name}' for the TMS procedures (TmsProcedures:ConnectionStringName) is not configured.");

        services.AddSingleton(new TmsConnectionString(connectionString));
        services.AddSingleton<TmsProcedureCatalog>();
        services.AddScoped<TmsProcedureExecutor>();
        services.AddScoped<TmsNotificationRepository>();
        return services;
    }
}
