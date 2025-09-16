using Newtonsoft.Json;
using Serilog;

namespace VRCVideoCacher;

public class BulkPreCache
{
    private static readonly ILogger Log = Program.Logger.ForContext<BulkPreCache>();
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } }
    };

    // FileName and Url are required
    // LastModified and Size are optional
    // e.g. JSON response
    // [{"fileName":"--QOnlGckhs.mp4","url":"https:\/\/example.com\/--QOnlGckhs.mp4","lastModified":1631653260,"size":124029113},...]
    // ReSharper disable once ClassNeverInstantiated.Local
    private class DownloadInfo(string fileName, string url, double lastModified, long size)
    {
        public string FileName { get; set; } = fileName;
        public string Url { get; set; } = url;
        public double LastModified { get; set; } = lastModified;
        public long Size { get; set; } = size;

        public DateTime LastModifiedDate => new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)
            .AddSeconds(LastModified);
        public string FilePath => Path.Combine(CacheManager.CachePath, FileName);
    }
    
    public static async Task DownloadFileList()
    {
        foreach (var url in ConfigManager.Config.PreCacheUrls)
        {
            using var response = await HttpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Log.Information("Failed to download {Url}: {ResponseStatusCode}", url, response.StatusCode);
                return;
            }

            var content = await response.Content.ReadAsStringAsync();
            var files = JsonConvert.DeserializeObject<List<DownloadInfo>>(content);
            if (files == null || files.Count == 0)
            {
                Log.Information("No files to download for {URL}", url);
                return;
            }
            await DownloadVideos(files);
            Log.Information("All {count} files for {URL} are up to date.", files.Count, url);
        }
    }

    private static async Task DownloadVideos(List<DownloadInfo> files)
    {
        var fileCount = files.Count;
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            if (string.IsNullOrEmpty(file.FileName))
                continue;

            try
            {
                if (File.Exists(file.FilePath))
                {
                    var fileInfo = new FileInfo(file.FilePath);
                    var lastWriteTime = File.GetLastWriteTimeUtc(file.FilePath);
                    if ((file.LastModified > 0 && file.LastModifiedDate != lastWriteTime) ||
                        (file.Size > 0 && file.Size != fileInfo.Length))
                    {
                        var percentage = Math.Round((double)index / fileCount * 100, 2);
                        Log.Information("Progress: {Percentage}%", percentage);
                        Log.Information("Updating {FileName}", file.FileName);
                        await DownloadFile(file);
                    }
                }
                else
                {
                    var percentage = Math.Round((double)index / fileCount * 100, 2);
                    Log.Information("Progress: {Percentage}%", percentage);
                    Log.Information("Downloading {FileName}", file.FileName);
                    await DownloadFile(file);
                }
            }
            catch (HttpRequestException ex)
            {
                Log.Error("Error downloading {FileName}: {ExMessage}", file.FileName, ex.Message);
            }
        }
    }
    
    private static async Task DownloadFile(DownloadInfo fileInfo)
    {
        using var response = await HttpClient.GetAsync(fileInfo.Url);
        if (!response.IsSuccessStatusCode)
        {
            Log.Information("Failed to download {Url}: {ResponseStatusCode}", fileInfo.Url, response.StatusCode);
            return;
        }
        var fileStream = new FileStream(fileInfo.FilePath, FileMode.Create, FileAccess.Write);
        await response.Content.CopyToAsync(fileStream);
        fileStream.Close();
        if (fileInfo.LastModified > 0)
        {
            await Task.Delay(10);
            File.SetLastWriteTimeUtc(fileInfo.FilePath, fileInfo.LastModifiedDate);
            File.SetCreationTimeUtc(fileInfo.FilePath, fileInfo.LastModifiedDate);
            File.SetLastAccessTimeUtc(fileInfo.FilePath, fileInfo.LastModifiedDate);
        }
    }
}