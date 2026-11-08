namespace DiscImageStudio.Burning;

/// <summary>Placeholder for the Windows IMAPI2 recorder referenced by the shared view models'
/// convenience constructors. The GTK frontend always passes <see cref="LinuxOpticalDiscBurner"/>
/// explicitly, so this type is never used on Linux.</summary>
public sealed class WindowsImapiBurner : IOpticalDiscBurner
{
    private static PlatformNotSupportedException NotSupported() =>
        new("IMAPI2 recording is only available on Windows.");

    public Task<IReadOnlyList<OpticalBurnDevice>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
        throw NotSupported();

    public Task<long> GetMediaSectorCountAsync(string deviceId, OpticalBurnMediaKind mediaKind,
        CancellationToken cancellationToken = default) => throw NotSupported();

    public Task<IReadOnlyList<OpticalWriteSpeed>> GetSupportedWriteSpeedsAsync(string deviceId,
        OpticalBurnMediaKind mediaKind, CancellationToken cancellationToken = default) => throw NotSupported();

    public Task<OpticalBurnResult> BurnAsync(OpticalBurnRequest request, IProgress<OpticalBurnProgress>? progress = null,
        CancellationToken cancellationToken = default) => throw NotSupported();
}
