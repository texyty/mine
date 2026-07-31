using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace MyCustomLauncher;

public sealed class ContentService {
    private const int MaximumAttempts = 3;
    private readonly HttpClient client;
    private readonly Uri contentBase;
    private readonly string gameDir;

    public ContentService(string baseUrl,string gameDir) {
        contentBase=new Uri(baseUrl.TrimEnd('/')+"/");
        client=new HttpClient{Timeout=TimeSpan.FromMinutes(10)};
        this.gameDir=Path.GetFullPath(gameDir);
    }

    public async Task<ContentManifest> SynchronizeAsync(string manifestPath,IProgress<ContentProgress> progress,CancellationToken ct=default) {
        progress.Report(new ContentProgress(0,"Получение списка файлов…",0,0,0,0));
        using var stream=await client.GetStreamAsync(new Uri(contentBase,manifestPath),ct);
        var manifest=await JsonSerializer.DeserializeAsync<ContentManifest>(stream,cancellationToken:ct)
            ?? throw new InvalidDataException("Некорректный manifest.json");
        if(string.IsNullOrWhiteSpace(manifest.MainClass)||manifest.Files.Count==0)
            throw new InvalidDataException("Manifest не содержит mainClass или файлов");

        long totalBytes=manifest.Files.Sum(file=>file.Size);
        long completedBytes=0;
        for(int i=0;i<manifest.Files.Count;i++) {
            ct.ThrowIfCancellationRequested();
            var file=manifest.Files[i];
            ValidateManifestFile(file);
            string target=ResolveSafePath(file.Path);
            if(!await IsValidAsync(target,file,ct)) {
                await DownloadWithRetryAsync(file,target,i,manifest.Files.Count,completedBytes,totalBytes,progress,ct);
            }
            completedBytes+=file.Size;
            progress.Report(CreateProgress(completedBytes,totalBytes,$"Проверено: {file.Path}",i+1,manifest.Files.Count));
        }
        return manifest;
    }

    private async Task DownloadWithRetryAsync(ManifestFile file,string target,int fileIndex,int fileCount,long completedBytes,
                                               long totalBytes,IProgress<ContentProgress> progress,CancellationToken ct) {
        Exception? lastError=null;
        for(int attempt=1;attempt<=MaximumAttempts;attempt++) {
            try {
                await DownloadAsync(file,target,fileIndex,fileCount,completedBytes,totalBytes,progress,ct);
                return;
            } catch(Exception ex) when(ex is HttpRequestException or IOException or InvalidDataException) {
                lastError=ex;
                if(attempt==MaximumAttempts)break;
                progress.Report(CreateProgress(completedBytes,totalBytes,$"Повтор {attempt + 1}/{MaximumAttempts}: {file.Path}",fileIndex,fileCount));
                await Task.Delay(TimeSpan.FromMilliseconds(400*attempt),ct);
            }
        }
        throw new IOException($"Не удалось загрузить {file.Path} после {MaximumAttempts} попыток",lastError);
    }

    private void ValidateManifestFile(ManifestFile file) {
        if(string.IsNullOrWhiteSpace(file.Path)||file.Size<0||file.Sha256.Length!=64||!file.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException($"Некорректная запись manifest: {file.Path}");
    }

    private string ResolveSafePath(string relative) {
        string normalized=relative.Replace('/',Path.DirectorySeparatorChar);
        string full=Path.GetFullPath(Path.Combine(gameDir,normalized));
        string prefix=gameDir.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Manifest содержит небезопасный путь");
        return full;
    }

    private static async Task<bool> IsValidAsync(string path,ManifestFile file,CancellationToken ct) {
        if(!File.Exists(path)||new FileInfo(path).Length!=file.Size)return false;
        await using var input=File.OpenRead(path);
        return string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(input,ct)),file.Sha256,StringComparison.OrdinalIgnoreCase);
    }

    private async Task DownloadAsync(ManifestFile file,string target,int fileIndex,int fileCount,long completedBytes,
                                     long totalBytes,IProgress<ContentProgress> progress,CancellationToken ct) {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary=target+".download";
        try {
            using var response=await client.GetAsync(new Uri(contentBase,file.Path.Replace('\\','/')),HttpCompletionOption.ResponseHeadersRead,ct);
            response.EnsureSuccessStatusCode();
            await using var input=await response.Content.ReadAsStreamAsync(ct);
            await using var output=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None,81920,true);
            var buffer=new byte[81920];
            long received=0;
            int read;
            while((read=await input.ReadAsync(buffer,ct))>0) {
                await output.WriteAsync(buffer.AsMemory(0,read),ct);
                received+=read;
                progress.Report(CreateProgress(completedBytes+received,totalBytes,$"Загрузка: {file.Path}",fileIndex,fileCount));
            }
            await output.FlushAsync(ct);
            output.Close();
            if(!await IsValidAsync(temporary,file,ct))throw new InvalidDataException($"Контрольная сумма не совпала: {file.Path}");
            File.Move(temporary,target,true);
        } finally {
            if(File.Exists(temporary))File.Delete(temporary);
        }
    }

    private static ContentProgress CreateProgress(long completed,long total,string message,int files,int fileCount) {
        double percent=total==0?100:Math.Clamp(completed*100d/total,0,100);
        return new ContentProgress(percent,message,files,fileCount,completed,total);
    }
}
