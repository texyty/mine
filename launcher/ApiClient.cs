using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
namespace MyCustomLauncher;
public sealed class ApiClient {
    private readonly HttpClient client;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive=true };
    public ApiClient(string baseUrl) { client=new HttpClient { BaseAddress=new Uri(baseUrl.TrimEnd('/')+"/"), Timeout=TimeSpan.FromSeconds(20) }; }
    public async Task<LauncherLoginResponse> LoginAsync(string username,string password,string hwid,CancellationToken ct=default) {
        try { using var response=await client.PostAsJsonAsync("api/launcher/login",new LauncherLoginRequest(username,password,hwid),ct); if(!response.IsSuccessStatusCode) throw await CreateException(response,ct); return await response.Content.ReadFromJsonAsync<LauncherLoginResponse>(Json,ct) ?? throw new LauncherApiException("Сервер вернул пустой ответ"); }
        catch(HttpRequestException ex){throw new LauncherApiException("Нет связи с сервером. Проверьте интернет и повторите попытку.",ex);} catch(TaskCanceledException ex) when(!ct.IsCancellationRequested){throw new LauncherApiException("Сервер не ответил вовремя.",ex);}
    }
    public async Task<bool> ValidateAsync(string token,CancellationToken ct=default) {
        using var request=new HttpRequestMessage(HttpMethod.Post,"api/launcher/session/validate"); request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        try { using var response=await client.SendAsync(request,ct); return response.IsSuccessStatusCode; } catch(HttpRequestException){return false;} catch(TaskCanceledException){return false;}
    }
    public async Task<bool> IsHealthyAsync(CancellationToken ct=default) {
        try { using var response=await client.GetAsync("health",ct); return response.IsSuccessStatusCode; }
        catch(HttpRequestException){return false;} catch(TaskCanceledException){return false;}
    }
    private static async Task<LauncherApiException> CreateException(HttpResponseMessage response,CancellationToken ct) { var error=await response.Content.ReadFromJsonAsync<ApiError>(Json,ct); return new LauncherApiException(error?.Detail ?? $"Ошибка сервера: {(int)response.StatusCode}"); }
}
public sealed class LauncherApiException(string message,Exception? inner=null):Exception(message,inner);
