using System.IO;
using System.Security.Cryptography;

namespace MyGears.Core;

public static class VerifiedAppUpdate
{
    public static void CopyInitialData(string source, string target)
    {
        if (File.Exists(source) && !File.Exists(target)) File.Copy(source, target, false);
    }

    public static bool SameContent(string source, string target)
    {
        if (!File.Exists(target) || new FileInfo(source).Length != new FileInfo(target).Length) return false;
        using var a = File.OpenRead(source);
        using var b = File.OpenRead(target);
        return SHA256.HashData(a).SequenceEqual(SHA256.HashData(b));
    }

    // Stage on the target volume, verify, then replace atomically. A locked target is an error.
    public static void Install(string source, string target)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("Không tìm thấy bản MyGears nguồn.", source);
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return;
        if (SameContent(source, target)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var staged = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, staged);
            if (!SameContent(source, staged)) throw new IOException("Bản cập nhật không khớp SHA-256. Chưa thay file đang dùng.");
            if (File.Exists(target)) File.Replace(staged, target, null);
            else File.Move(staged, target);
            if (!SameContent(source, target)) throw new IOException("Không xác minh được bản MyGears sau cập nhật.");
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }
}
