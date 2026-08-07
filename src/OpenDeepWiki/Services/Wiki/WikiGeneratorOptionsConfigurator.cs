using Microsoft.Extensions.Configuration;

namespace OpenDeepWiki.Services.Wiki;

public static class WikiGeneratorOptionsConfigurator
{
    public static void Apply(WikiGeneratorOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        options.CatalogProviderId = ResolveStringValue(
            configuration,
            $"{WikiGeneratorOptions.SectionName}:CatalogProviderId",
            options.CatalogProviderId);
        options.CatalogModel = ResolveStringValue(
            configuration,
            $"{WikiGeneratorOptions.SectionName}:CatalogModel",
            options.CatalogModel) ?? options.CatalogModel;

        options.ContentProviderId = ResolveStringValue(
            configuration,
            $"{WikiGeneratorOptions.SectionName}:ContentProviderId",
            options.ContentProviderId);
        options.ContentModel = ResolveStringValue(
            configuration,
            $"{WikiGeneratorOptions.SectionName}:ContentModel",
            options.ContentModel) ?? options.ContentModel;

        options.TranslationProviderId = ResolveStringValue(
            configuration,
            $"{WikiGeneratorOptions.SectionName}:TranslationProviderId",
            options.TranslationProviderId);
        options.TranslationModel = ResolveStringValue(
            configuration,
            $"{WikiGeneratorOptions.SectionName}:TranslationModel",
            options.TranslationModel);

        options.Languages = ResolveStringValue(
            configuration,
            $"{WikiGeneratorOptions.SectionName}:Languages",
            options.Languages,
            // Docker deployments historically exposed this setting as a flat
            // WIKI_LANGUAGES variable. Keep that form working alongside the
            // standard WikiGenerator__Languages binding.
            "WIKI_LANGUAGES");
    }

    private static string? ResolveStringValue(
        IConfiguration configuration,
        string sectionKey,
        string? fallbackValue,
        params string[] aliases)
    {
        var configuredValue = configuration[sectionKey];
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            foreach (var alias in aliases)
            {
                configuredValue = configuration[alias];
                if (!string.IsNullOrWhiteSpace(configuredValue))
                {
                    break;
                }
            }
        }

        return !string.IsNullOrWhiteSpace(configuredValue)
            ? configuredValue
            : fallbackValue;
    }
}
