using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace VRCVideoCacher.Services;

public class VvcConfigService
{
    public static VvcConfig CurrentConfig = new();
    private static readonly HttpClient httpClient;

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
            if (deserialized != null)
                CurrentConfig = deserialized;
        }
    }
}

public class VvcConfig
{
    [JsonPropertyName("motd")]
    public string Motd { get; set; } = string.Empty;

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; } = 7;
}