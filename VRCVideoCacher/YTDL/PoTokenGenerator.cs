using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Win32;
using Serilog;

namespace VRCVideoCacher.YTDL;

public class PoTokenGenerator
{
    private static readonly ILogger Log = Program.Logger.ForContext<PoTokenGenerator>();
    private const string PythonUrl = "https://www.python.org/ftp/python/3.13.3/python-3.13.3-embed-amd64.zip"; // it's a pain to get the latest version
    private const string GetPipUrl = "https://bootstrap.pypa.io/get-pip.py";
    private const string YouTubeTrustedSessionGeneratorUrl = "https://github.com/iv-org/youtube-trusted-session-generator/archive/refs/heads/master.zip";
    private static readonly string WorkingDirectory = Path.Combine(Environment.CurrentDirectory, "Utils", "python");
    private static readonly string YtdlPoTokenPath = Path.Combine(Program.CurrentProcessPath, "PoToken.txt");
    private static string _lastPoToken = string.Empty;
    private static int _setupInProgress;
    private static int _tokenGenerationInProgress;
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } }
    };

    static PoTokenGenerator()
    {
        if (File.Exists(YtdlPoTokenPath))
            _lastPoToken = File.ReadAllText(YtdlPoTokenPath).Trim();
    }

    private static async Task TrySetupEnvironment()
    {
        if (!IsChromeInstalled())
            throw new Exception("Google Chrome is not installed. Please install it to use PoToken generator.");
        
        if (Directory.Exists(WorkingDirectory))
            return;
        
        Directory.CreateDirectory(WorkingDirectory);
        var pythonZip = await DownloadFile(PythonUrl);
        Log.Information("Extracting Python zip.");
        ZipFile.ExtractToDirectory(pythonZip, WorkingDirectory);
        File.Delete(YtdlPoTokenPath);
        
        Log.Information("Editing Python PATH.");
        var pathFile = Directory.EnumerateFiles(WorkingDirectory, "*._pth", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (pathFile == null)
            throw new Exception("Python *._pth file not found. Cancelling environment setup.");
        await File.AppendAllTextAsync(pathFile, "import site");
        
        var pipFile = await DownloadFile(GetPipUrl);
        Log.Information("Installing pip...");
        await RunPythonScript(pipFile);
        File.Delete(pipFile);
        
        Log.Information("Installing setup tools...");
        await RunPythonPip("install -t \".\\Lib\\site-packages\" setuptools");
        
        var trustedSessionGeneratorZip = await DownloadFile(YouTubeTrustedSessionGeneratorUrl);
        Log.Information("Extracting YouTubeTrustedSessionGenerator.");
        ZipFile.ExtractToDirectory(trustedSessionGeneratorZip, WorkingDirectory);
        File.Delete(trustedSessionGeneratorZip);
        
        Log.Information("Editing potoken-generator.py.");
        var trustedSessionGeneratorDir = Path.Combine(WorkingDirectory, "youtube-trusted-session-generator-master");
        var poTokenGenerator = Path.Combine(trustedSessionGeneratorDir, "potoken-generator.py");
        var lines = new List<string>
        {
            "import sys",
            "sys.path.append(\".\\\\youtube-trusted-session-generator-master\")"
        };
        var linesToAdd = string.Join(Environment.NewLine, lines);
        var poTokenFileContent = await File.ReadAllTextAsync(poTokenGenerator);
        await File.WriteAllTextAsync(poTokenGenerator, linesToAdd + Environment.NewLine + poTokenFileContent);
        
        Log.Information("Installing requirements...");
        await RunPythonPip($"install -t \".\\Lib\\site-packages\" -r \"{trustedSessionGeneratorDir}\\requirements.txt\"");
        
        Log.Information("Python environment setup complete.");
    }
    
    public static async Task TryGeneratePoToken()
    {
        if (string.IsNullOrEmpty(_lastPoToken))
            await GeneratePoToken();
    }
    
    public static async Task<string> GetPoToken()
    {
        await TryGeneratePoToken();
        return _lastPoToken;
    }

    public static async Task GeneratePoToken()
    {
        if (Interlocked.CompareExchange(ref _setupInProgress, 1, 0) != 0 ||
            Interlocked.CompareExchange(ref _tokenGenerationInProgress, 1, 0) != 0)
            return;

        try
        {
            try
            {
                await TrySetupEnvironment();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to setup Python environment: {Message}", ex.Message);
                try
                {
                    Directory.Delete(WorkingDirectory, true);
                }
                catch (Exception deleteEx)
                {
                    Log.Error(deleteEx, "Failed to delete Python environment: {Message}", deleteEx.Message);
                }
                return;
            }
            finally
            {
                Interlocked.Exchange(ref _setupInProgress, 0);
            }

            Log.Information("Generating new PoToken...");
            var poTokenGenerator = Path.Combine(WorkingDirectory, "youtube-trusted-session-generator-master", "potoken-generator.py");
            string output;
            try
            {
                output = await RunPythonScript(poTokenGenerator, "--oneshot");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to generate PoToken: {Message}", ex.Message);
                return;
            }

            var lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            var poToken = string.Empty;
            foreach (var line in lines)
            {
                var index = line.IndexOf("po_token: ", StringComparison.Ordinal);
                if (index < 0)
                    continue;

                poToken = line.Substring(index + 10).Trim();
                break;
            }
            if (string.IsNullOrEmpty(poToken))
            {
                Log.Error("Failed to generate PoToken.");
                return;
            }

            _lastPoToken = poToken;
            await File.WriteAllTextAsync(YtdlPoTokenPath, poToken);
            Log.Information("Generated new PoToken.");
        }
        finally
        {
            Interlocked.Exchange(ref _tokenGenerationInProgress, 0);
        }
    }

    private static async Task<string> DownloadFile(string url)
    {
        var fileName = Path.GetFileName(url);
        Log.Information("Downloading {FileName}...", fileName);
        var filePath = Path.Combine(WorkingDirectory, fileName);
        
        if (File.Exists(filePath))
        {
            Log.Warning("File {fileName} already exists. Skipping download.", fileName);
            return filePath;
        }

        using var response = await HttpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fileStream);
        
        Log.Information("Downloaded {fileName}.", fileName);
        return filePath;
    }

    private static async Task RunPythonPip(string args)
    {
        const string pipExe = "pip.exe";
        var pythonPipPath = Path.Combine(WorkingDirectory, "Scripts", pipExe);
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = pythonPipPath,
            WorkingDirectory = WorkingDirectory,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.Start();
        while (!process.StandardOutput.EndOfStream)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (line != null)
                Log.Debug("{pip}: " + line, pipExe);
        }
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new Exception($"Python pip failed with error: {error}");
    }
    
    private static async Task<string> RunPythonScript(string scriptPath, string args = "")
    {
        var scriptName = Path.GetFileName(scriptPath);
        var pythonExe = Path.Combine(WorkingDirectory, "python.exe");
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = pythonExe,
            WorkingDirectory = WorkingDirectory,
            Arguments = $"\"{scriptPath}\" {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.Start();
        var output = string.Empty;
        while (!process.StandardOutput.EndOfStream)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (line == null)
                continue;
            
            Log.Debug("{scriptName}: " + line, scriptName);
            output += line + Environment.NewLine;
        }
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new Exception($"Python script failed with error: {error}");
        return output;
    }
    
    private static bool IsChromeInstalled()
    {
        const string chromePath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe";
#pragma warning disable CA1416
        return Registry.LocalMachine.OpenSubKey(chromePath) != null;
#pragma warning restore CA1416
    }
}