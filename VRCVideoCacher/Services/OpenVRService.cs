using System.Reflection;
using Serilog;
using Valve.VR;

namespace VRCVideoCacher.Services;

public class OpenVRService
{
    private static readonly ILogger Logger = Program.Logger.ForContext<OpenVRService>();

    public static void Start(string dataPath)
    {
        // Register as background app on a background thread with retry so SteamVR
        // doesn't activate theater mode, even if vrserver starts after us.
        Task.Run(async () =>
        {
            bool retry = true;

            while (retry)
            {
                retry = false;
                var initError = EVRInitError.None;
                try
                {
                    OpenVR.Init(ref initError, EVRApplicationType.VRApplication_Background);
                }
                catch (Exception ex)
                {
                    Logger.Warning("Exception during init: {Msg}", ex.Message);
                    return;
                }

                switch (initError)
                {
                    case EVRInitError.None:
                        var manifestPath = Path.Combine(dataPath, "manifest.vrmanifest");
                        await using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VRCVideoCacher.manifest.vrmanifest")!)
                        await using (var file = File.Create(manifestPath))
                            await stream.CopyToAsync(file);
                        var manifestError = OpenVR.Applications.AddApplicationManifest(manifestPath, false);
                        if (manifestError != EVRApplicationError.None)
                        {
                            Logger.Warning("Failed to register startup manifest: {Error}", manifestError);
                        }
                        else
                        {
                            if (OpenVR.Applications.IsApplicationInstalled("com.github.ellyvr.vrcvideocacher"))
                            {
                                Logger.Information("Startup manifest registered successfully");

                                Logger.Information("{AutoLaunchState} steamvr auto-launch", ConfigManager.Config.StartWithSteamVr ? "Enabling" : "Disabling");
                                OpenVR.Applications.SetApplicationAutoLaunch("com.github.ellyvr.vrcvideocacher", ConfigManager.Config.StartWithSteamVr);
                            }
                            else
                            {
                                Logger.Warning("Failed to register startup manifest");
                            }
                        }
                        break;
                    // Only retry if vrserver just isn't running yet
                    case EVRInitError.Init_HmdNotFound or EVRInitError.Init_HmdNotFoundPresenceFailed or EVRInitError.Init_NoServerForBackgroundApp:
                        await Task.Delay(TimeSpan.FromSeconds(5));
                        retry = true;
                        break;
                    default:
                        Logger.Information("Not available: {Error}", initError);
                        break;
                }

                try
                {
                    OpenVR.Shutdown();
                }
                catch (Exception ex)
                {
                    Logger.Warning("Exception during shutdown: {Msg}", ex.Message);
                    return;
                }
            }
        });
    }
}
