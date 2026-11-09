using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DiscImageStudio.Localization;

namespace DiscImageStudio.Burning;

/// <summary>Linux recorder backend. Device discovery reads sysfs; media inspection and
/// recording are delegated to the libburnia tools, which speak MMC to the drive:
/// <c>cdrskin</c> (or a cdrecord-compatible <c>wodim</c>/<c>cdrecord</c>) writes CD audio and
/// CD data in session-at-once mode, <c>xorriso</c> inspects media and writes DVD and BD-R.
/// Content is streamed through the tool's standard input, so no full image is staged on
/// disk — the same streaming model as the Windows IMAPI2 backend.</summary>
public sealed partial class LinuxOpticalDiscBurner : IOpticalDiscBurner
{
    private const string SysBlock = "/sys/class/block";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(60);

    /// <summary>One-line description of the recording tools for the burn page.</summary>
    public static string BackendSummary
    {
        get
        {
            string cd = FindCdRecorder() is { } cdTool ? Path.GetFileName(cdTool) : L.T("Gnome.ToolMissing");
            string dvd = FindTool("xorriso") is { } xorriso ? Path.GetFileName(xorriso) : L.T("Gnome.ToolMissing");
            return L.Format("Gnome.BurnBackendSummary", cd, dvd);
        }
    }

    public Task<IReadOnlyList<OpticalBurnDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        List<OpticalBurnDevice> devices = [];
        if (Directory.Exists(SysBlock))
        {
            foreach (string entry in Directory.EnumerateDirectories(SysBlock, "sr*").OrderBy(name => name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = Path.GetFileName(entry);
                string node = "/dev/" + name;
                if (!Path.Exists(node)) continue;
                string vendor = ReadSysText(Path.Combine(entry, "device", "vendor"));
                string model = ReadSysText(Path.Combine(entry, "device", "model"));
                string label = string.Join(' ', new[] { vendor, model }.Where(part => part.Length > 0));
                devices.Add(new OpticalBurnDevice(node,
                    label.Length == 0 ? node : $"{label} ({node})",
                    MountPoints(node)));
            }
        }

        return Task.FromResult<IReadOnlyList<OpticalBurnDevice>>(devices);
    }

    public async Task<long> GetMediaSectorCountAsync(string deviceId, OpticalBurnMediaKind mediaKind,
        CancellationToken cancellationToken = default) =>
        (await GetMediaCapacityAsync(deviceId, mediaKind, cancellationToken).ConfigureAwait(false)).TotalSectors;

    /// <summary>Reads the media extent with <c>xorriso -toc</c>: "Media blocks : R readable ,
    /// W writable , O overall". Only blank media is accepted for recording.</summary>
    public async Task<OpticalMediaCapacity> GetMediaCapacityAsync(string deviceId, OpticalBurnMediaKind mediaKind,
        CancellationToken cancellationToken = default)
    {
        ValidateDevice(deviceId);
        string xorriso = FindTool("xorriso")
            ?? throw new InvalidOperationException(L.Format("Gnome.ToolRequired", "xorriso"));
        ToolResult result = await RunToolAsync(xorriso,
            ["-abort_on", "NEVER", "-return_with", "SORRY", "0", "-outdev", deviceId, "-toc"],
            cancellationToken).ConfigureAwait(false);
        string output = result.Output;
        if (MediaStatusRegex().Match(output) is { Success: true } status
            && !status.Groups[1].Value.Contains("blank", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(L.Format("Gnome.MediaNotBlank", status.Groups[1].Value.Trim()));
        }

        Match blocks = MediaBlocksRegex().Match(output);
        if (!blocks.Success)
        {
            throw new InvalidOperationException(L.Format("Gnome.MediaUnreadable", LastLines(output, 4)));
        }

        long writable = long.Parse(blocks.Groups[2].Value, CultureInfo.InvariantCulture);
        long overall = long.Parse(blocks.Groups[3].Value, CultureInfo.InvariantCulture);
        if (overall <= 0 || writable <= 0)
        {
            throw new InvalidOperationException(L.Format("Gnome.MediaUnreadable", LastLines(output, 4)));
        }

        return new OpticalMediaCapacity(Math.Max(overall, writable), writable);
    }

    /// <summary>Lists write speeds with <c>xorriso -list_speeds</c> ("Write speed : 5540k , 4.0x").</summary>
    public async Task<IReadOnlyList<OpticalWriteSpeed>> GetSupportedWriteSpeedsAsync(string deviceId,
        OpticalBurnMediaKind mediaKind, CancellationToken cancellationToken = default)
    {
        ValidateDevice(deviceId);
        string xorriso = FindTool("xorriso")
            ?? throw new InvalidOperationException(L.Format("Gnome.ToolRequired", "xorriso"));
        ToolResult result = await RunToolAsync(xorriso,
            ["-abort_on", "NEVER", "-return_with", "SORRY", "0", "-outdev", deviceId, "-list_speeds"],
            cancellationToken).ConfigureAwait(false);
        int sectorBytes = OpticalBurnMedia.GetSectorBytes(mediaKind);
        // libburn reports CD speeds against 176.4 kB/s per 1x (2352-byte sectors at 75/s).
        int rateSectorBytes = mediaKind is OpticalBurnMediaKind.CdAudio or OpticalBurnMediaKind.CdData ? 2352 : sectorBytes;
        SortedSet<int> sectorsPerSecond = [];
        foreach (Match match in WriteSpeedRegex().Matches(result.Output))
        {
            long kiloBytes = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            int sectors = (int)Math.Round(kiloBytes * 1000.0 / rateSectorBytes);
            if (sectors > 0) sectorsPerSecond.Add(sectors);
        }

        return sectorsPerSecond.Select(sectors => new OpticalWriteSpeed(sectors, RotationTypeIsPureCav: false)).ToArray();
    }

    public async Task<OpticalBurnResult> BurnAsync(OpticalBurnRequest request, IProgress<OpticalBurnProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        ValidateDevice(request.DeviceId);
        if (request.MediaKind == OpticalBurnMediaKind.CdAudio)
        {
            // The CD audio command runs with -force (see BuildCommand), which turns off cdrskin's own
            // size test, so the track is checked against the loaded disc here instead.
            OpticalMediaCapacity capacity = await GetMediaCapacityAsync(request.DeviceId, request.MediaKind, cancellationToken)
                .ConfigureAwait(false);
            long sectors = request.ContentLength / OpticalBurnMedia.GetSectorBytes(request.MediaKind);
            if (sectors > capacity.WritableSectors)
                throw new InvalidOperationException(L.Format("Gnome.TrackTooLong", sectors, capacity.WritableSectors));
        }

        (string tool, IReadOnlyList<string> arguments) = BuildCommand(request);
        Stopwatch elapsed = Stopwatch.StartNew();
        progress?.Report(new OpticalBurnProgress("device", L.Format("Gnome.BurnStarting", Path.GetFileName(tool)), 0, request.ContentLength));
        progress?.Report(new OpticalBurnProgress("diagnostic", Path.GetFileName(tool) + " " + string.Join(' ', arguments), 0, request.ContentLength));

        using Process process = new()
        {
            StartInfo = CreateStartInfo(tool, arguments, redirectInput: true),
            EnableRaisingEvents = true,
        };
        StringBuilder log = new();
        process.OutputDataReceived += (_, args) => CaptureLine(args.Data, log, progress, request.ContentLength);
        process.ErrorDataReceived += (_, args) => CaptureLine(args.Data, log, progress, request.ContentLength);
        if (!process.Start()) throw new InvalidOperationException(L.Format("Gnome.ToolFailed", tool, string.Empty));
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using CancellationTokenRegistration registration = cancellationToken.Register(() => TryKill(process));
        ProgressStream input = new(process.StandardInput.BaseStream, request.ContentLength, progress);
        long written;
        try
        {
            await Task.Factory.StartNew(() =>
            {
                try
                {
                    request.ProduceContent(input, cancellationToken);
                    input.Flush();
                }
                finally
                {
                    input.Close();
                }
            }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).ConfigureAwait(false);
            written = input.BytesWritten;
            if (written != request.ContentLength)
            {
                throw new InvalidDataException(L.Format("Gnome.BurnShortContent", written, request.ContentLength));
            }

            progress?.Report(new OpticalBurnProgress("finalize", L.T("Gnome.BurnFinalizing"), written, request.ContentLength));
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!process.HasExited)
        {
            TryKill(process);
            throw;
        }
        catch (IOException) when (process.HasExited && process.ExitCode != 0)
        {
            // The tool quit (broken pipe); its own diagnostics explain why.
            throw new IOException(L.Format("Gnome.ToolFailed", Path.GetFileName(tool), LastLines(log.ToString(), 8)));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            throw new IOException(L.Format("Gnome.ToolFailed", Path.GetFileName(tool), LastLines(log.ToString(), 8)));
        }

        progress?.Report(new OpticalBurnProgress("complete", L.T("Gnome.BurnComplete"), written, request.ContentLength));
        return new OpticalBurnResult(request.DeviceId, request.MediaKind, written, elapsed.Elapsed, request.WriteSpeed);
    }

    /// <summary>Builds the recorder command line for one request.</summary>
    internal static (string Tool, IReadOnlyList<string> Arguments) BuildCommand(OpticalBurnRequest request)
    {
        List<string> arguments = [];
        string? speed = request.WriteSpeed is { } writeSpeed
            ? "speed=" + Math.Max(1, (int)Math.Floor(writeSpeed.GetMultiplier(request.MediaKind))).ToString(CultureInfo.InvariantCulture)
            : null;
        string size = "tsize=" + request.ContentLength.ToString(CultureInfo.InvariantCulture);
        switch (request.MediaKind)
        {
            case OpticalBurnMediaKind.CdAudio:
            case OpticalBurnMediaKind.CdData:
            {
                string tool = FindCdRecorder()
                    ?? throw new InvalidOperationException(L.Format("Gnome.ToolRequired", "cdrskin"));
                arguments.AddRange(["-v", "dev=" + request.DeviceId, "gracetime=0", "-sao"]);
                if (speed is not null) arguments.Add(speed);
                // Audio arrives as headerless little-endian PCM; cdrecord-style tools expect
                // big-endian unless told otherwise with -swab.
                // -force: cdrskin predicts the session size of a stdin audio track as bytes / 2048
                // instead of bytes / 2352 and would reject a full-length track (413262s > 359847s on an
                // 80-minute CD-R). BurnAsync checks the real sector count against the disc first.
                if (request.MediaKind == OpticalBurnMediaKind.CdAudio) arguments.AddRange(["-force", "-audio", "-swab"]);
                else arguments.Add("-data");
                arguments.AddRange(["-nopad", size, "-"]);
                return (tool, arguments);
            }

            case OpticalBurnMediaKind.DvdData:
            case OpticalBurnMediaKind.BluRayData:
            {
                string tool = FindTool("xorriso")
                    ?? throw new InvalidOperationException(L.Format("Gnome.ToolRequired", "xorriso"));
                arguments.AddRange(["-as", "cdrecord", "-v", "dev=" + request.DeviceId, "gracetime=0"]);
                if (speed is not null) arguments.Add(speed);
                // BD-R: write sequentially without defect management, like the Windows path.
                if (request.MediaKind == OpticalBurnMediaKind.BluRayData) arguments.Add("stream_recording=on");
                else arguments.Add("-sao");
                arguments.AddRange(["-nopad", size, "-"]);
                return (tool, arguments);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.MediaKind, "Unknown optical media kind.");
        }
    }

    internal static string? FindCdRecorder() =>
        FindTool("cdrskin") ?? FindTool("wodim") ?? FindTool("cdrecord");

    internal static string? FindTool(string name)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        foreach (string directory in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static void ValidateDevice(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (!deviceId.StartsWith("/dev/", StringComparison.Ordinal) || deviceId.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException(L.T("Gnome.InvalidRecorder"), nameof(deviceId));
    }

    private static string ReadSysText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static IReadOnlyList<string> MountPoints(string node)
    {
        try
        {
            return File.ReadLines("/proc/mounts")
                .Select(line => line.Split(' '))
                .Where(parts => parts.Length > 1 && parts[0] == node)
                .Select(parts => parts[1].Replace("\\040", " ", StringComparison.Ordinal))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static ProcessStartInfo CreateStartInfo(string tool, IEnumerable<string> arguments, bool redirectInput)
    {
        ProcessStartInfo info = new(tool)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        // Untranslated tool output keeps the parsers stable.
        info.Environment["LC_ALL"] = "C";
        return info;
    }

    private sealed record ToolResult(int ExitCode, string Output);

    private static async Task<ToolResult> RunToolAsync(string tool, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using Process process = new() { StartInfo = CreateStartInfo(tool, arguments, redirectInput: false) };
        if (!process.Start()) throw new InvalidOperationException(L.Format("Gnome.ToolFailed", tool, string.Empty));
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new ToolResult(process.ExitCode, await stdout.ConfigureAwait(false) + "\n" + await stderr.ConfigureAwait(false));
    }

    private static void CaptureLine(string? line, StringBuilder log, IProgress<OpticalBurnProgress>? progress, long total)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (log) log.AppendLine(line);
        // Progress lines repeat every second; only keep the tool's other messages in the log.
        if (line.Contains("written", StringComparison.OrdinalIgnoreCase)
            && line.Contains("fifo", StringComparison.OrdinalIgnoreCase)) return;
        progress?.Report(new OpticalBurnProgress("diagnostic", line.Trim(), 0, total));
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    internal static string LastLines(string text, int count) =>
        string.Join('\n', text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(count));

    [GeneratedRegex(@"^Media status\s*:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex MediaStatusRegex();

    [GeneratedRegex(@"Media blocks\s*:\s*(\d+)\s*readable\s*,\s*(\d+)\s*writable\s*,\s*(\d+)\s*overall")]
    private static partial Regex MediaBlocksRegex();

    [GeneratedRegex(@"^Write speed\s*[LHlh]?\s*:\s*(\d+)k\s*,", RegexOptions.Multiline)]
    private static partial Regex WriteSpeedRegex();

    /// <summary>Counts bytes on their way to the recorder and reports write progress.</summary>
    private sealed class ProgressStream(Stream inner, long total, IProgress<OpticalBurnProgress>? progress) : Stream
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastReportBytes;
        private TimeSpan _lastReportTime;

        public long BytesWritten { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            BytesWritten += buffer.Length;
            TimeSpan now = _clock.Elapsed;
            if (progress is not null && (now - _lastReportTime >= TimeSpan.FromMilliseconds(250) || BytesWritten == total))
            {
                double seconds = (now - _lastReportTime).TotalSeconds;
                double? rate = seconds > 0 ? (BytesWritten - _lastReportBytes) / seconds : null;
                _lastReportTime = now;
                _lastReportBytes = BytesWritten;
                progress.Report(new OpticalBurnProgress("write", string.Empty, BytesWritten, total, rate));
            }
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
