using System.IO;
using System.Text.Json;

namespace MyGears.Core;

/// <summary>
/// Model thông tin một tài khoản game
/// </summary>
public class GameAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Game { get; set; } = "Valorant";
    public string Label { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Định dạng tk:mk (ví dụ: pro_player:MatKhau123)
    /// </summary>
    public string Combo => $"{Username}:{Password}";
}

/// <summary>
/// Cấu trúc lưu trữ trong file accounts.enc (đã được mã hóa)
/// </summary>
public class VaultEnvelope
{
    public string PinHash { get; set; } = string.Empty;
    public string PinSalt { get; set; } = string.Empty;
    public string EncryptedAccountsJson { get; set; } = string.Empty;
}

/// <summary>
/// Quản lý việc lưu trữ, mã hóa và giải mã tài khoản game
/// </summary>
public static class AccountVaultService
{
    private static string VaultFilePath
    {
        get
        {
            var inApp = Path.Combine(UsbPathResolver.AppDir, "accounts.enc");
            if (File.Exists(inApp)) return inApp;
            var inRoot = Path.Combine(UsbPathResolver.UsbRoot, "accounts.enc");
            if (File.Exists(inRoot)) return inRoot;
            return inApp;
        }
    }

    /// <summary>
    /// Kiểm tra kho tài khoản đã được thiết lập mã PIN chưa
    /// </summary>
    public static bool IsVaultConfigured => File.Exists(VaultFilePath);

    /// <summary>
    /// Kiểm tra mã PIN nhập vào
    /// </summary>
    public static bool VerifyPin(string pin)
    {
        if (!IsVaultConfigured) return false;
        try
        {
            var json = File.ReadAllText(VaultFilePath);
            var envelope = JsonSerializer.Deserialize<VaultEnvelope>(json);
            if (envelope == null) return false;
            return SecurityService.VerifyPin(pin, envelope.PinHash, envelope.PinSalt);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Thiết lập mã PIN lần đầu
    /// </summary>
    public static void SetupMasterPin(string pin)
    {
        var (hash, salt) = SecurityService.HashPin(pin);
        var emptyAccountsJson = JsonSerializer.Serialize(new List<GameAccount>());
        var encrypted = SecurityService.Encrypt(emptyAccountsJson, pin);

        var envelope = new VaultEnvelope
        {
            PinHash = hash,
            PinSalt = salt,
            EncryptedAccountsJson = encrypted
        };

        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(VaultFilePath, json);

        SyncToUsbIfConnected();
    }

    /// <summary>
    /// Tải và giải mã danh sách tài khoản bằng mã PIN
    /// </summary>
    public static List<GameAccount> LoadAccounts(string pin)
    {
        if (!IsVaultConfigured) return [];
        try
        {
            var json = File.ReadAllText(VaultFilePath);
            var envelope = JsonSerializer.Deserialize<VaultEnvelope>(json);
            if (envelope == null) return [];

            if (!SecurityService.VerifyPin(pin, envelope.PinHash, envelope.PinSalt))
                throw new UnauthorizedAccessException("Mã PIN không chính xác.");

            if (string.IsNullOrEmpty(envelope.EncryptedAccountsJson))
                return [];

            var plainJson = SecurityService.Decrypt(envelope.EncryptedAccountsJson, pin);
            return JsonSerializer.Deserialize<List<GameAccount>>(plainJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Mã hóa và lưu danh sách tài khoản bằng mã PIN
    /// </summary>
    public static void SaveAccounts(List<GameAccount> accounts, string pin)
    {
        var (hash, salt) = SecurityService.HashPin(pin);
        var plainJson = JsonSerializer.Serialize(accounts);
        var encrypted = SecurityService.Encrypt(plainJson, pin);

        var envelope = new VaultEnvelope
        {
            PinHash = hash,
            PinSalt = salt,
            EncryptedAccountsJson = encrypted
        };

        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(VaultFilePath, json);

        SyncToUsbIfConnected();
    }

    /// <summary>
    /// Đổi mã PIN
    /// </summary>
    public static bool ChangePin(string oldPin, string newPin)
    {
        if (!VerifyPin(oldPin)) return false;
        var accounts = LoadAccounts(oldPin);
        SaveAccounts(accounts, newPin);
        return true;
    }

    private static void SyncToUsbIfConnected()
    {
        try
        {
            var usbRoot = UsbPathResolver.FindConnectedUsbRoot();
            if (!string.IsNullOrEmpty(usbRoot))
            {
                var usbAppDir = Path.Combine(usbRoot, "App");
                if (Directory.Exists(usbAppDir) && File.Exists(VaultFilePath))
                {
                    var dest = Path.Combine(usbAppDir, "accounts.enc");
                    File.Copy(VaultFilePath, dest, overwrite: true);
                }
            }
        }
        catch { }
    }
}
