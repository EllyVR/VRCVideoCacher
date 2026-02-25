using Newtonsoft.Json;

namespace VRCVideoCacher.Services;

public class VvcConfigService
{
    public static VvcConfig CurrentConfig = new VvcConfig();
    private static HttpClient httpClient;

    static VvcConfigService()
    {
        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", $"VRCVideoCacher v{Program.Version}");
    }
    public static async Task GetConfig()
    {
        var req = await httpClient.GetAsync("https://vvc.ellyvr.dev/api/v1/config");
        if (req.IsSuccessStatusCode)
        {
            var deserialized = JsonConvert.DeserializeObject<VvcConfig>(await req.Content.ReadAsStringAsync());
            if(deserialized!=null)
                CurrentConfig = deserialized;
        }
    }
}

public class VvcConfig
{
    public string motd { get; set; } = string.Empty;
    public int retryCount { get; set; } = 7;
}