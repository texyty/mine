using System.Security.Cryptography;
using System.Text;
namespace MyCustomLauncher;
public static class SecureTokenStore {
    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("MyCustomLauncher.TokenStore.v1"));
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyCustomLauncher");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "session.dat");
    public static void Save(string token) { Directory.CreateDirectory(DirectoryPath); byte[] protectedBytes=ProtectedData.Protect(Encoding.UTF8.GetBytes(token),Entropy,DataProtectionScope.CurrentUser); File.WriteAllBytes(FilePath, protectedBytes); }
    public static string? Load() { try { if(!File.Exists(FilePath))return null; return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath),Entropy,DataProtectionScope.CurrentUser)); } catch(CryptographicException){Clear();return null;} }
    public static void Clear() { if(File.Exists(FilePath)) File.Delete(FilePath); }
}

