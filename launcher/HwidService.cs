using Microsoft.Win32;
using System.Management;
using System.Security.Cryptography;
using System.Text;
namespace MyCustomLauncher;
public static class HwidService {
    public static string GetHwid(string applicationSalt) {
        string cpu = QueryWmi("Win32_Processor", "ProcessorId");
        string board = QueryWmi("Win32_BaseBoard", "SerialNumber");
        string machine = ReadMachineGuid();
        if (string.IsNullOrWhiteSpace(cpu) && string.IsNullOrWhiteSpace(board) && string.IsNullOrWhiteSpace(machine)) throw new InvalidOperationException("Не удалось получить идентификаторы компьютера");
        string canonical = $"CPU:{Normalize(cpu)}|BOARD:{Normalize(board)}|MACHINE:{Normalize(machine)}|APP:{applicationSalt}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
    private static string QueryWmi(string className, string property) {
        try { using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {className}"); foreach (ManagementObject item in searcher.Get()) { var value=item[property]?.ToString(); if (!string.IsNullOrWhiteSpace(value)) return value; } } catch (ManagementException) { } catch (UnauthorizedAccessException) { }
        return "";
    }
    private static string ReadMachineGuid() {
        const string path=@"SOFTWARE\Microsoft\Cryptography";
        using var key=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(path) ?? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(path);
        return key?.GetValue("MachineGuid")?.ToString() ?? "";
    }
    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}

