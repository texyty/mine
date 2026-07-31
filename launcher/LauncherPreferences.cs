using System.Text.Json;
namespace MyCustomLauncher;

public sealed class LauncherPreferences {
    public string Username { get; set; } = "";
    public int RamGb { get; set; } = 4;

    private static readonly string DirectoryPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MyCustomLauncher");
    private static readonly string FilePath=Path.Combine(DirectoryPath,"preferences.json");

    public static LauncherPreferences Load() {
        try { return File.Exists(FilePath)?JsonSerializer.Deserialize<LauncherPreferences>(File.ReadAllText(FilePath))??new():new(); }
        catch(JsonException){return new();} catch(IOException){return new();}
    }

    public void Save() {
        Directory.CreateDirectory(DirectoryPath);
        string temporary=FilePath+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(temporary,FilePath,true);
    }
}

