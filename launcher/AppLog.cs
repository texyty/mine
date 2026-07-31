using System.Text;
namespace MyCustomLauncher;

public static class AppLog {
    private static readonly object Sync=new();
    private static readonly string DirectoryPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MyCustomLauncher","logs");
    public static void Info(string message)=>Write("INFO",message);
    public static void Error(string message,Exception exception)=>Write("ERROR",$"{message}: {exception.GetType().Name}: {exception.Message}");
    private static void Write(string level,string message) {
        try { lock(Sync){Directory.CreateDirectory(DirectoryPath);string path=Path.Combine(DirectoryPath,$"launcher-{DateTime.UtcNow:yyyyMMdd}.log");File.AppendAllText(path,$"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}",Encoding.UTF8);} } catch(IOException) { } catch(UnauthorizedAccessException) { }
    }
}
