namespace MyCustomLauncher;

public static class JavaResolver {
    public static string Resolve(string configuredPath) {
        if (Path.IsPathRooted(configuredPath) && File.Exists(configuredPath)) return configuredPath;
        if (!string.Equals(configuredPath,"javaw.exe",StringComparison.OrdinalIgnoreCase)) return configuredPath;
        string? javaHome=Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome)) {
            string candidate=Path.Combine(javaHome,"bin","javaw.exe");
            if(File.Exists(candidate))return candidate;
        }
        foreach(string directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries)) {
            try { string candidate=Path.Combine(directory.Trim(),"javaw.exe"); if(File.Exists(candidate))return candidate; } catch(ArgumentException) { }
        }
        throw new FileNotFoundException("Java не найдена. Установите Java и задайте JavaPath в appsettings.json.");
    }
}

