using System.Diagnostics;
namespace MyCustomLauncher;
public static class GameLauncher {
    public static Process Start(string javaPath,string gameDir,ContentManifest manifest,string username,string token,int ramGb) {
        string versionDir=Path.Combine(gameDir,"versions",manifest.Version); string jar=Path.Combine(versionDir,$"{manifest.Version}.jar");
        var jars=Directory.Exists(Path.Combine(gameDir,"libs"))?Directory.GetFiles(Path.Combine(gameDir,"libs"),"*.jar",SearchOption.AllDirectories):[];
        string classpath=string.Join(Path.PathSeparator,jars.Append(jar)); if(!File.Exists(jar))throw new FileNotFoundException("Не найден JAR клиента",jar);
        var info=new ProcessStartInfo{FileName=javaPath,WorkingDirectory=gameDir,UseShellExecute=false};
        info.ArgumentList.Add($"-Xmx{ramGb}G");info.ArgumentList.Add("-Djava.library.path="+Path.Combine(gameDir,"natives"));info.ArgumentList.Add("-cp");info.ArgumentList.Add(classpath);info.ArgumentList.Add(manifest.MainClass);
        Add(info,"--username",username);Add(info,"--version",manifest.Version);Add(info,"--gameDir",gameDir);Add(info,"--assetsDir",Path.Combine(gameDir,"assets"));Add(info,"--accessToken","0");Add(info,"--userType","legacy");Add(info,"--customToken",token);
        return Process.Start(info)??throw new InvalidOperationException("Java не удалось запустить");
    }
    private static void Add(ProcessStartInfo info,string name,string value){info.ArgumentList.Add(name);info.ArgumentList.Add(value);}
}
