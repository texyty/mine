using Microsoft.Extensions.Configuration;

namespace MyCustomLauncher;

public sealed class LauncherConfig {
    public string ApiBaseUrl { get; init; } = "";
    public string ContentBaseUrl { get; init; } = "";
    public string WebBaseUrl { get; init; } = "";
    public string ManifestPath { get; init; } = "manifest.json";
    public string HwidApplicationSalt { get; init; } = "";
    public string JavaPath { get; init; } = "javaw.exe";

    public static LauncherConfig Load() {
        var source = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", false).Build();
        var result = new LauncherConfig {
            ApiBaseUrl = source["ApiBaseUrl"] ?? "",
            ContentBaseUrl = source["ContentBaseUrl"] ?? "",
            WebBaseUrl = source["WebBaseUrl"] ?? "",
            ManifestPath = source["ManifestPath"] ?? "manifest.json",
            HwidApplicationSalt = source["HwidApplicationSalt"] ?? "",
            JavaPath = source["JavaPath"] ?? "javaw.exe"
        };
        if (!Uri.TryCreate(result.ApiBaseUrl, UriKind.Absolute, out _) ||
            !Uri.TryCreate(result.ContentBaseUrl, UriKind.Absolute, out _) ||
            !Uri.TryCreate(result.WebBaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("В конфигурации указан некорректный URL.");
        return result;
    }
}
