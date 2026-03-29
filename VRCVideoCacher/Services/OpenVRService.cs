using Serilog;
using Valve.VR;

namespace VRCVideoCacher.Services;

public class OpenVRService
{
    private static readonly ILogger Logger = Log.ForContext<OpenVRService>();
    private const string OpenVrAppKey = "com.github.ellyvr.vrcvideocacher";
    private static string OpenVrManifestPath => Path.Join(Program.DataPath, "manifest.vrmanifest");

    public static void InitOvr()
    {
        // OpenVR.Applications.SetApplicationAutoLaunch(OpenVrAppKey, true);
        try
        {
            // extract manifest to data folder
            if (!File.Exists(OpenVrManifestPath))
            {
                using var stream = Program.GetVrManifest();
                using var fileStream = new FileStream(OpenVrManifestPath, FileMode.Create, FileAccess.Write);
                stream.CopyTo(fileStream);
            }
            var err = EVRInitError.None;
            var openVr = OpenVR.Init(ref err, EVRApplicationType.VRApplication_Background);
            if (err != EVRInitError.None)
            {
                Logger.Information("OpenVR initialization failed with error: {Error}. OpenVR is not running, skipping manifest registration.", err);
                return;
            }
            OpenVR.Applications.AddApplicationManifest(OpenVrManifestPath, false);
        }
        catch (Exception ex)
        {
            Logger.Information(ex, "OpenVR is not installed, skipping manifest registration.");
        }
    }
}