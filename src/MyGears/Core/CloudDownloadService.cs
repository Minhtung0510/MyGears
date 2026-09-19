using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace MyGears.Core;

/// <summary>
/// Dịch vụ tải driver và công cụ từ đám mây (GitHub Releases)
/// khi USB không chứa sẵn thư mục Download offline để tối ưu dung lượng USB xuống siêu nhẹ (~2.3 MB).
/// </summary>
public static class CloudDownloadService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    public const string GitHubRepoOwner = "Minhtung0510";
    public const string GitHubRepoName = "MyGears";
    public const string DefaultReleaseTag = "v1.0.0";

    public static readonly Dictionary<string, string> KnownDownloadUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["scyrox"] = $"https://github.com/{GitHubRepoOwner}/{GitHubRepoName}/releases/download/{DefaultReleaseTag}/ScyRox.zip",
        ["webview2runtime"] = $"https://github.com/{GitHubRepoOwner}/{GitHubRepoName}/releases/download/{DefaultReleaseTag}/WebView2Runtime.zip",
        ["webview2"] = $"https://github.com/{GitHubRepoOwner}/{GitHubRepoName}/releases/download/{DefaultReleaseTag}/WebView2Runtime.zip"
    };

    public static string? GetCloudUrlForComponent(string componentId, string folderName)
    {
        var key = folderName.ToLowerInvariant();
        if (KnownDownloadUrls.TryGetValue(key, out var url))
            return url;

        if (key.Contains("scyrox")) return KnownDownloadUrls["scyrox"];
        if (key.Contains("webview")) return KnownDownloadUrls["webview2runtime"];

        return null;
    }

    /// <summary>
    /// Tải file zip từ GitHub và giải nén trực tiếp vào thư mục đích với tiến trình phần trăm.
    /// </summary>
    public static async Task<bool> DownloadAndExtractZipAsync(
        string downloadUrl,
        string destinationDir,
        Action<string, double>? onProgress = null)
    {
        string? tempZipPath = null;
        try
        {
            Directory.CreateDirectory(destinationDir);

            var tempDir = Path.GetTempPath();
            tempZipPath = Path.Combine(tempDir, $"mygears_dl_{Guid.NewGuid():N}.zip");

            onProgress?.Invoke("🌐 Đang kết nối tới đám mây GitHub…", 0.05);

            using (var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[16384];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;

                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        double progress = (double)totalRead / totalBytes.Value;
                        double mbRead = totalRead / (1024.0 * 1024.0);
                        double mbTotal = totalBytes.Value / (1024.0 * 1024.0);
                        onProgress?.Invoke($"⬇ Đang tải từ GitHub: {mbRead:0.#} MB / {mbTotal:0.#} MB ({progress:P0})…", Math.Min(0.85, 0.05 + progress * 0.8));
                    }
                    else
                    {
                        double mbRead = totalRead / (1024.0 * 1024.0);
                        onProgress?.Invoke($"⬇ Đang tải từ GitHub: {mbRead:0.#} MB…", 0.5);
                    }
                }
            }

            onProgress?.Invoke("📦 Đang giải nén driver vào máy tính…", 0.90);
            await Task.Run(() =>
            {
                ZipFile.ExtractToDirectory(tempZipPath, destinationDir, overwriteFiles: true);
            });

            onProgress?.Invoke("✅ Tải và giải nén thành công!", 0.98);
            return true;
        }
        catch (Exception ex)
        {
            onProgress?.Invoke($"❌ Lỗi tải từ GitHub: {ex.Message}", 0.0);
            return false;
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempZipPath) && File.Exists(tempZipPath))
            {
                try { File.Delete(tempZipPath); } catch { }
            }
        }
    }
}
