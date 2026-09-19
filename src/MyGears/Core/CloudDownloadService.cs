using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace MyGears.Core;

/// <summary>
/// Đại diện cho 1 file asset đính kèm trong GitHub Release
/// </summary>
public class CloudAssetInfo
{
    public string Name { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

/// <summary>
/// Dịch vụ tải driver và công cụ từ đám mây (GitHub Releases)
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
    /// Gọi trực tiếp GitHub Releases API để lấy danh sách mọi file tải lên Release mới nhất
    /// </summary>
    public static async Task<List<CloudAssetInfo>> FetchLatestReleaseAssetsAsync()
    {
        try
        {
            var apiUrl = $"https://api.github.com/repos/{GitHubRepoOwner}/{GitHubRepoName}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.UserAgent.ParseAdd("MyGearsApp");

            using var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return new List<CloudAssetInfo>();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("assets", out var assetsElem) || assetsElem.ValueKind != JsonValueKind.Array)
            {
                return new List<CloudAssetInfo>();
            }

            var result = new List<CloudAssetInfo>();
            foreach (var asset in assetsElem.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                long size = asset.TryGetProperty("size", out var sizeProp) ? sizeProp.GetInt64() : 0;

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
                {
                    result.Add(new CloudAssetInfo
                    {
                        Name = name,
                        DownloadUrl = url,
                        SizeBytes = size
                    });
                }
            }
            return result;
        }
        catch
        {
            return new List<CloudAssetInfo>();
        }
    }

    /// <summary>
    /// Tải file từ GitHub (hỗ trợ giải nén .zip tự động hoặc lưu file .exe trực tiếp).
    /// </summary>
    public static async Task<bool> DownloadCloudAssetAsync(
        string downloadUrl,
        string destinationDir,
        Action<string, double>? onProgress = null)
    {
        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(destinationDir);

            var uri = new Uri(downloadUrl);
            var rawFileName = Path.GetFileName(uri.LocalPath);
            bool isZip = rawFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

            var tempDir = Path.GetTempPath();
            var ext = Path.GetExtension(rawFileName);
            tempPath = Path.Combine(tempDir, $"mygears_dl_{Guid.NewGuid():N}{ext}");

            onProgress?.Invoke($"🌐 Đang kết nối tới đám mây GitHub ({rawFileName})…", 0.05);

            using (var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

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
                        onProgress?.Invoke($"⬇ Đang tải {rawFileName}: {mbRead:0.#} MB / {mbTotal:0.#} MB ({progress:P0})…", Math.Min(0.85, 0.05 + progress * 0.8));
                    }
                    else
                    {
                        double mbRead = totalRead / (1024.0 * 1024.0);
                        onProgress?.Invoke($"⬇ Đang tải {rawFileName}: {mbRead:0.#} MB…", 0.5);
                    }
                }
            }

            if (isZip)
            {
                onProgress?.Invoke($"📦 Đang giải nén {rawFileName} vào máy tính…", 0.90);
                await Task.Run(() =>
                {
                    ZipFile.ExtractToDirectory(tempPath, destinationDir, overwriteFiles: true);
                });
            }
            else
            {
                onProgress?.Invoke($"💾 Đang lưu file {rawFileName}…", 0.90);
                var destFile = Path.Combine(destinationDir, rawFileName);
                File.Copy(tempPath, destFile, overwrite: true);
            }

            onProgress?.Invoke("✅ Tải và xử lý hoàn tất!", 0.98);
            return true;
        }
        catch (Exception ex)
        {
            onProgress?.Invoke($"❌ Lỗi tải từ GitHub: {ex.Message}", 0.0);
            return false;
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    /// <summary>
    /// Tương thích ngược với DownloadAndExtractZipAsync
    /// </summary>
    public static Task<bool> DownloadAndExtractZipAsync(
        string downloadUrl,
        string destinationDir,
        Action<string, double>? onProgress = null) => DownloadCloudAssetAsync(downloadUrl, destinationDir, onProgress);
}
