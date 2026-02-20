namespace VRCVideoCacher.Elevator;

class Program
{
    public static void Main(string[] args)
    {
        if (args.Contains("--addhost"))
        {
            HostsManager.Add();
        }

        if (args.Contains("--removehost"))
        {
            HostsManager.Remove();            
        }
    }


}