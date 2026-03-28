using OVRSharp;
using Serilog;
using Valve.VR;

namespace VRCVideoCacher.Services;

public class OpenVRService
{
    private static Application _application;
    private static ILogger _logger;
    
    public static void InitOVR()
    {
        _logger = Log.ForContext<OpenVRService>();
        try
        {
            OpenVR.Applications.AddApplicationManifest(Path.Join(Program.CurrentProcessPath, "manifest.vrmanifest"), false);
        }
        catch (Exception e)
        {
            _logger.Information(e.ToString());
        }
    }
}