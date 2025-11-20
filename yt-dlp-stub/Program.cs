using System.Net.Sockets;

namespace yt_dlp;

internal static class Program
{
    private static string _logFilePath = string.Empty;
    private const string BaseUrl = "http://127.0.0.1:9696";

    private static void WriteLog(string message)
    {
        try
        {
            using var sw = new StreamWriter(_logFilePath, true);
            sw.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
        }
        catch (Exception)
        {
            // ignore
        }
    }

    public static async Task Main(string[] args)
    {
        var appDataPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low", @"VRChat\VRChat\Tools");
        _logFilePath = Path.Combine(appDataPath, "ytdl.log");
        
        var url = string.Empty;
        var avPro = true;
        string source = "vrchat";
        foreach (var arg in args)
        {
            if (arg.Contains("[protocol^=http]"))
            {
                avPro = false;
                continue;
            }

            if (arg.Contains("-J"))
            {
                source = "resonite";
                continue;
            }
            
            if (!arg.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;
            
            url = arg;
            break;
        }
        
        WriteLog($"Starting with args: {string.Join(" ", args)}, avPro: {avPro}, source: {source}");
        
        if (string.IsNullOrEmpty(url))
        {
            WriteLog("[Error] No URL found in arguments");
            await Console.Error.WriteLineAsync("ERROR: [VRCVideoCacher] No URL found in arguments");
            Environment.ExitCode = 1;
            return;
        }
        
        try
        {
            using var httpClient = new HttpClient();
            var inputUrl = Uri.EscapeDataString(url);
            var response = await httpClient.GetAsync($"{BaseUrl}/api/getvideo?url={inputUrl}&avpro={avPro}&source={source}");
            var output = await response.Content.ReadAsStringAsync();
            WriteLog($"[Response] {output}");
            if (!response.IsSuccessStatusCode)
                throw new Exception(output);
            Console.WriteLine(output);
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException socketEx && socketEx.SocketErrorCode == SocketError.ConnectionRefused)
        {
            WriteLog("[Error] Connection refused. Is the server running?");
            await Console.Error.WriteLineAsync("ERROR: [VRCVideoCacher] Connection refused. Is VRCVideoCacher running?");
            var ytdlPath = Path.Combine(appDataPath, "yt-dlp.exe");
            if (File.Exists(ytdlPath) && File.GetAttributes(ytdlPath).HasFlag(FileAttributes.ReadOnly))
            {
                var attr = File.GetAttributes(ytdlPath);
                attr &= ~FileAttributes.ReadOnly;
                File.SetAttributes(ytdlPath, attr);
            }
            Environment.ExitCode = 1;
        }
        catch (Exception ex)
        {
            WriteLog($"[Error] {ex}");
            await Console.Error.WriteLineAsync($"ERROR: [VRCVideoCacher] {ex.GetType().Name}: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }
}