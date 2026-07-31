using System.Text.Json.Serialization;
namespace MyCustomLauncher;

public sealed record LauncherLoginRequest(string Username, string Password, string Hwid);
public sealed record LauncherWebAuthStartRequest(string Hwid);
public sealed class LauncherWebAuthStartResponse { [JsonPropertyName("request_id")] public string RequestId { get; set; } = ""; [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; } }
public sealed class LauncherWebAuthPollResponse { [JsonPropertyName("status")] public string Status { get; set; } = "pending"; [JsonPropertyName("session_token")] public string? SessionToken { get; set; } [JsonPropertyName("username")] public string? Username { get; set; } [JsonPropertyName("detail")] public string? Detail { get; set; } }
public sealed class LauncherLoginResponse {
    [JsonPropertyName("session_token")] public string SessionToken { get; set; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("username")] public string Username { get; set; } = "";
}
public sealed class ApiError { [JsonPropertyName("detail")] public string? Detail { get; set; } }
public sealed class ContentManifest {
    [JsonPropertyName("version")] public string Version { get; set; } = "MyCustomClient";
    [JsonPropertyName("mainClass")] public string MainClass { get; set; } = "";
    [JsonPropertyName("files")] public List<ManifestFile> Files { get; set; } = [];
}
public sealed class ManifestFile {
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}
public sealed record ContentProgress(double Percent,string Message,int CompletedFiles,int TotalFiles,long CompletedBytes,long TotalBytes);
