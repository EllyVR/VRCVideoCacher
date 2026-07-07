namespace VRCVideoCacher.Utils;

public class TempDir : IDisposable
{
    private const String Prefix = "VRCVideoCacher-";

    private readonly DirectoryInfo _di = Directory.CreateTempSubdirectory(Prefix);
    private bool _disposed;

    ~TempDir()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public string FullName => _di.FullName;

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        try
        {
            // Assume the temporary subdirectory still exists but is empty.
            _di.Delete(false);
        }
        catch
        {
            // If we couldn't delete it (e.g. because it wasn't empty), then leave it around for debugging. The OS will
            // eventually clean this up for us.
        }
        _disposed = true;
    }
}