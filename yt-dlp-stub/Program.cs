﻿namespace yt_dlp;

internal static class Program
{
    private static string _logFilePath = string.Empty;
    private const string _baseUrl = "http://localhost:9696";

    private static void WriteLog(string message)
    {
        using var sw = new StreamWriter(_logFilePath, true);
        sw.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
    }

    public static async Task Main(string[] args)
    {
        _logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low", @"VRChat\VRChat\Tools", "ytdl.log");
        
        var url = string.Empty;
        var avPro = true;
        foreach (var arg in args)
        {
            if (arg.Contains("[protocol^=http]"))
            {
                avPro = false;
                continue;
            }
            
            if (!arg.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;
            
            url = arg;
            break;
        }
        
        WriteLog($"Starting with args: {string.Join(" ", args)}, avPro: {avPro}");
        
        if (string.IsNullOrEmpty(url))
        {
            WriteLog("[Error] No URL found in arguments");
            return;
        }
        
        try
        {
            using var httpClient = new HttpClient();
            var inputUrl = Uri.EscapeDataString(url);
            var output = await httpClient.GetStringAsync($"{_baseUrl}/api/getvideo?url={inputUrl}&avpro={avPro}");
            WriteLog("Response: " + output);
            Console.WriteLine(output);
            if (!output.Trim().StartsWith("http"))
            {
                Environment.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            WriteLog($"[Error] {ex.Message}");
            Environment.ExitCode = 1;
        }
    }
}