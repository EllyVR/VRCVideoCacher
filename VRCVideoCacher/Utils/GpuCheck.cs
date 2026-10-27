using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

namespace VRCVideoCacher.Utils;

/// <summary>
/// Detects AMD GPUs on Windows, where VP9 videos decoded through Media Foundation appear corrupted.
/// An AMD iGPU next to an NVIDIA card doesn't count, since VRChat renders and decodes on the NVIDIA one.
/// </summary>
internal static class GpuCheck
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(GpuCheck));

    private const uint AmdVendorId = 0x1002;
    private const uint NvidiaVendorId = 0x10DE;
    private const uint SoftwareAdapterFlag = 2;
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);

    private static readonly Lazy<bool> AmdGpu = new(Detect);

    public static bool HasAmdGpu => AmdGpu.Value;

    private static bool Detect()
    {
        // TODO: Add Linux support for AMD GPU detection
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            var adapters = EnumerateAdapters();
            var hasAmd = adapters.Any(a => a.VendorId == AmdVendorId);
            var hasNvidia = adapters.Any(a => a.VendorId == NvidiaVendorId);
            Log.Information("Detected GPUs: {Gpus}", string.Join(", ", adapters.Select(a => a.Description).Distinct()));
            return hasAmd && !hasNvidia;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to detect GPUs.");
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static List<DxgiAdapterDesc1> EnumerateAdapters()
    {
        var adapters = new List<DxgiAdapterDesc1>();
        var iid = typeof(IDxgiFactory1).GUID;
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out var factory));
        try
        {
            for (uint i = 0; ; i++)
            {
                var hr = factory.EnumAdapters1(i, out var adapter);
                if (hr == DxgiErrorNotFound)
                    break;
                Marshal.ThrowExceptionForHR(hr);

                try
                {
                    if (adapter.GetDesc1(out var desc) >= 0 && (desc.Flags & SoftwareAdapterFlag) == 0)
                        adapters.Add(desc);
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }

        return adapters;
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IDxgiFactory1 factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint Flags;
    }

    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDxgiFactory1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumAdapters();
        void MakeWindowAssociation();
        void GetWindowAssociation();
        void CreateSwapChain();
        void CreateSoftwareAdapter();

        [PreserveSig]
        int EnumAdapters1(uint index, out IDxgiAdapter1 adapter);
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDxgiAdapter1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumOutputs();
        void GetDesc();
        void CheckInterfaceSupport();

        [PreserveSig]
        int GetDesc1(out DxgiAdapterDesc1 desc);
    }
}
