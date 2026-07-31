using Microsoft.Extensions.Configuration;
namespace MyCustomLauncher;
public sealed class LauncherConfig {
    public string ApiBaseUrl { get; init; } = "";
    public string ContentBaseUrl { get; init; } = "";
    public string ManifestPath { get; init; } = "manifest.json";
    public string HwidApplicationSalt { get; init; } = "";
    public string JavaPath { get; init; } = "javaw.exe";
    public static LauncherConfig Load() {
        var config = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", false).Build();
        var result = new LauncherConfig {
            ApiBaseUrl=config["ApiBaseUrl"] ?? "", ContentBaseUrl=config["ContentBaseUrl"] ?? "",
            ManifestPath=config["ManifestPath"] ?? "manifest.json", HwidApplicationSalt=config["HwidApplicationSalt"] ?? "",
            JavaPath=config["JavaPath"] ?? "javaw.exe"
        };
        if (!Uri.TryCreate(result.ApiBaseUrl, UriKind.Absolute, out _) || !Uri.TryCreate(result.ContentBaseUrl, UriKind.Absolute, out _)) throw new InvalidOperationException("В конфигурации указан некорректный URL");
        return result;
    }
}
