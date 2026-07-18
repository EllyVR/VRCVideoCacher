using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

namespace VRCVideoCacher.Utils;

/// <summary>
/// Proves the machine can actually decode <b>Opus inside MP4</b> — which is what the SABR restream
/// muxes (<c>SabrSegmentMuxer</c>) and hands to AVPro.
///
/// Windows' Media Foundation MP4 source (<c>mfmp4srcsnk.dll</c>) could not map the <c>Opus</c> sample
/// entry to a decoder for years (microsoft/media-foundation#36). Support arrived in build .8524
/// (KB5089573) — but as a <b>Controlled Feature Rollout</b>, enabled server-side per machine. So a
/// version comparison is NOT a valid test: the DLL can be new enough while the feature is still off.
/// The only sound check is to decode something and see whether audio actually comes out, which is
/// exactly what this does, through the same stack AVPro uses.
///
/// The failure is silent and vicious. In a desktop player it shows as video with mute audio, which is
/// what makes it diagnosable at all — but <b>in VRChat the video simply never loads</b>: AVPro fetches
/// the init segment and a couple of media segments, fails, and restarts forever. Every log — ours and
/// VRChat's — shows nothing but healthy requests, so there is no way to tell it apart from a network
/// or extraction problem. Diagnosing it from scratch took hours; this turns it into one line at startup.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class OpusMp4Check
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(OpusMp4Check));

    // The build that first carries Opus-in-MP4 support, used only to tell "your Windows is too old"
    // apart from "you have it but the rollout hasn't reached you". Never used to decide pass/fail.
    private const int SupportedBuildRevision = 8524;

    private const string ProbeResourceName = "VRCVideoCacher.opus_probe.mp4";

    public static void Run()
    {
        try
        {
            if (Probe())
            {
                Log.Debug("Opus-in-MP4 decode check passed");
                return;
            }
        }
        catch (Exception ex)
        {
            // A probe that cannot run is not proof of a broken machine — say so and move on rather
            // than firing a scary popup at someone whose playback is fine.
            Log.Warning(ex, "Could not verify Opus-in-MP4 support; skipping the check");
            return;
        }

        Log.Error("This PC cannot play Opus audio in MP4, so YouTube videos will FAIL TO LOAD in " +
                  "VRChat. This is a Windows problem, not a VRCVideoCacher one. {Advice} " +
                  "Windows Media Foundation file version: {Version}.",
            Advice(), Mp4SourceVersion() ?? "unknown");
    }

    /// <summary>Below .8524 the code isn't there at all; at or above, it's the rollout gate.</summary>
    private static string Advice()
    {
        var revision = Mp4SourceRevision();
        if (revision is null)
            return "Please install the latest Windows updates and restart.";

        return revision < SupportedBuildRevision
            ? "Your Windows is missing the update that adds this (KB5089573). " +
              "Please install all pending Windows updates and restart your PC."
            : "Your Windows has the update, but Microsoft enables this feature gradually and it has " +
              "not reached this PC yet. Install any pending Windows updates and try again later.";
    }

    private static string? Mp4SourceVersion()
    {
        var path = Path.Combine(Environment.SystemDirectory, "mfmp4srcsnk.dll");
        return File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).FileVersion : null;
    }

    private static int? Mp4SourceRevision()
    {
        var path = Path.Combine(Environment.SystemDirectory, "mfmp4srcsnk.dll");
        if (!File.Exists(path))
            return null;
        var info = FileVersionInfo.GetVersionInfo(path);
        return info.FilePrivatePart == 0 ? null : info.FilePrivatePart;
    }

    /// <summary>
    /// Decodes the embedded fixture to PCM via a Media Foundation SourceReader. True only when audio
    /// samples with actual bytes come out — asking for a PCM output type is not enough on its own,
    /// since the failure can surface as a stream that resolves but yields nothing.
    /// </summary>
    private static bool Probe()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vvc_opus_probe_{Environment.ProcessId}.mp4");
        try
        {
            using (var resource = Program.GetEmbeddedResource(ProbeResourceName))
            using (var file = File.Create(path))
                resource.CopyTo(file);

            return ProbeFile(path);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    private static bool ProbeFile(string path)
    {
        Check(MFStartup(MfVersion, MfstartupFull), nameof(MFStartup));
        try
        {
            Check(MFCreateSourceReaderFromURL(path, IntPtr.Zero, out var reader), nameof(MFCreateSourceReaderFromURL));
            try
            {
                // Deselect everything, then take only the audio stream: decoding the video too would
                // make this slower and could fail for reasons that have nothing to do with Opus.
                Check(reader.SetStreamSelection(MfSourceReaderAllStreams, false), "SetStreamSelection(all, false)");
                if (reader.SetStreamSelection(MfSourceReaderFirstAudioStream, true) < 0)
                    return false; // no audio stream at all — the MP4 source did not surface the Opus track

                Check(MFCreateMediaType(out var mediaType), nameof(MFCreateMediaType));
                try
                {
                    // Locals because interop needs these by ref, and static readonly fields cannot be.
                    var majorKey = MfMtMajorType;
                    var majorValue = MfMediaTypeAudio;
                    var subtypeKey = MfMtSubtype;
                    var subtypeValue = MfAudioFormatPcm;
                    Check(mediaType.SetGUID(ref majorKey, ref majorValue), "SetGUID(major)");
                    Check(mediaType.SetGUID(ref subtypeKey, ref subtypeValue), "SetGUID(subtype)");

                    // Requesting PCM makes MF resolve a decoder. On an unsupported machine this is
                    // where it gives up.
                    if (reader.SetCurrentMediaType(MfSourceReaderFirstAudioStream, IntPtr.Zero, mediaType) < 0)
                        return false;
                }
                finally
                {
                    Marshal.ReleaseComObject(mediaType);
                }

                return ReadsAudio(reader);
            }
            finally
            {
                Marshal.ReleaseComObject(reader);
            }
        }
        finally
        {
            MFShutdown();
        }
    }

    /// <summary>Reads until real PCM bytes appear, or the stream ends, or we run out of patience.</summary>
    private static bool ReadsAudio(IMFSourceReader reader)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var hr = reader.ReadSample(MfSourceReaderFirstAudioStream, 0, IntPtr.Zero,
                out var flags, out _, out var sample);
            if (hr < 0)
                return false;

            if (sample == IntPtr.Zero)
            {
                // End of stream without a single decoded byte means the decode never happened.
                if ((flags & MfSourceReaderfEndofstream) != 0)
                    return false;
                continue; // a gap or a format change; keep reading
            }

            try
            {
                var mediaSample = (IMFSample)Marshal.GetObjectForIUnknown(sample);
                try
                {
                    if (mediaSample.GetTotalLength(out var length) >= 0 && length > 0)
                        return true;
                }
                finally
                {
                    Marshal.ReleaseComObject(mediaSample);
                }
            }
            finally
            {
                Marshal.Release(sample);
            }
        }

        return false;
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr, new IntPtr(-1));
        _ = what;
    }

    // ---- Media Foundation interop -------------------------------------------------------------

    private const uint MfVersion = 0x00020070; // MF_SDK_VERSION 0x0002 << 16 | MF_API_VERSION 0x0070
    private const uint MfstartupFull = 0;
    private const uint MfSourceReaderAllStreams = 0xFFFFFFFE;
    private const uint MfSourceReaderFirstAudioStream = 0xFFFFFFFD;
    private const uint MfSourceReaderfEndofstream = 0x2;

    private static readonly Guid MfMtMajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MfMtSubtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MfMediaTypeAudio = new("73647561-0000-0010-8000-00aa00389b71");
    private static readonly Guid MfAudioFormatPcm = new("00000001-0000-0010-8000-00aa00389b71");

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(uint version, uint flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(out IMFMediaType type);

    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IMFSourceReader reader);

    [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(uint streamIndex, out bool selected);
        [PreserveSig] int SetStreamSelection(uint streamIndex, bool selected);
        [PreserveSig] int GetNativeMediaType(uint streamIndex, uint mediaTypeIndex, out IMFMediaType type);
        [PreserveSig] int GetCurrentMediaType(uint streamIndex, out IMFMediaType type);
        [PreserveSig] int SetCurrentMediaType(uint streamIndex, IntPtr reserved, IMFMediaType type);
        [PreserveSig] int SetCurrentPosition(ref Guid guidTimeFormat, IntPtr varPosition);
        [PreserveSig] int ReadSample(uint streamIndex, uint controlFlags, IntPtr actualStreamIndex,
            out uint streamFlags, out long timestamp, out IntPtr sample);
        [PreserveSig] int Flush(uint streamIndex);
        [PreserveSig] int GetServiceForStream(uint streamIndex, ref Guid guidService, ref Guid riid, out IntPtr service);
        [PreserveSig] int GetPresentationAttribute(uint streamIndex, ref Guid guidAttribute, IntPtr value);
    }

    // Only the vtable slots up to what we call need to be correct, but IMFMediaType derives from
    // IMFAttributes — so every inherited method has to be declared, in order, to keep them lined up.
    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaType
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out bool result);
        [PreserveSig] int Compare(IntPtr attributes, int matchType, out bool result);
        [PreserveSig] int GetUINT32(ref Guid key, out uint value);
        [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
        [PreserveSig] int GetDouble(ref Guid key, out double value);
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength(ref Guid key, out uint length);
        [PreserveSig] int GetString(ref Guid key, IntPtr value, uint size, IntPtr length);
        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);
        [PreserveSig] int GetBlobSize(ref Guid key, out uint size);
        [PreserveSig] int GetBlob(ref Guid key, IntPtr buffer, uint bufferSize, IntPtr blobSize);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out uint size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid riid, out IntPtr unknown);
        [PreserveSig] int SetItem(ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, uint value);
        [PreserveSig] int SetUINT64(ref Guid key, ulong value);
        [PreserveSig] int SetDouble(ref Guid key, double value);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob(ref Guid key, IntPtr buffer, uint size);
        [PreserveSig] int SetUnknown(ref Guid key, IntPtr unknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IntPtr dest);
        [PreserveSig] int GetMajorType(out Guid majorType);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSample
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out bool result);
        [PreserveSig] int Compare(IntPtr attributes, int matchType, out bool result);
        [PreserveSig] int GetUINT32(ref Guid key, out uint value);
        [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
        [PreserveSig] int GetDouble(ref Guid key, out double value);
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength(ref Guid key, out uint length);
        [PreserveSig] int GetString(ref Guid key, IntPtr value, uint size, IntPtr length);
        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);
        [PreserveSig] int GetBlobSize(ref Guid key, out uint size);
        [PreserveSig] int GetBlob(ref Guid key, IntPtr buffer, uint bufferSize, IntPtr blobSize);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out uint size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid riid, out IntPtr unknown);
        [PreserveSig] int SetItem(ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, uint value);
        [PreserveSig] int SetUINT64(ref Guid key, ulong value);
        [PreserveSig] int SetDouble(ref Guid key, double value);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob(ref Guid key, IntPtr buffer, uint size);
        [PreserveSig] int SetUnknown(ref Guid key, IntPtr unknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IntPtr dest);
        [PreserveSig] int GetSampleFlags(out uint flags);
        [PreserveSig] int SetSampleFlags(uint flags);
        [PreserveSig] int GetSampleTime(out long time);
        [PreserveSig] int SetSampleTime(long time);
        [PreserveSig] int GetSampleDuration(out long duration);
        [PreserveSig] int SetSampleDuration(long duration);
        [PreserveSig] int GetBufferCount(out uint count);
        [PreserveSig] int GetBufferByIndex(uint index, out IntPtr buffer);
        [PreserveSig] int ConvertToContiguousBuffer(out IntPtr buffer);
        [PreserveSig] int AddBuffer(IntPtr buffer);
        [PreserveSig] int RemoveBufferByIndex(uint index);
        [PreserveSig] int RemoveAllBuffers();
        [PreserveSig] int GetTotalLength(out uint length);
        [PreserveSig] int CopyToBuffer(IntPtr buffer);
    }
}
