using System.Diagnostics;

namespace Rea.Experiments;

public sealed record MeasurementLimits(TimeSpan Deadline, int StdoutBytes, int StderrBytes)
{
    public static MeasurementLimits Default { get; } = new(TimeSpan.FromSeconds(30), 64 * 1024 * 1024, 1024 * 1024);
}
public sealed record ProcessObservation(int ExitCode, byte[] Output, byte[] Error, double ElapsedMs, double? CpuMs, long? SampledPeakWorkingSetBytes);
public sealed class MeasurementProcessException(string reason, bool directProcessExited, Exception inner, Process? retainedProcess = null) : Exception(reason, inner)
{
    public bool DirectProcessExited { get; } = directProcessExited;
    // Failed cleanup retains the owner handle, rather than relying on a PID.
    public Process? RetainedProcess { get; } = retainedProcess;
}

public static class ProcessMeasurement
{
    public static async Task<ProcessObservation> RunAsync(ProcessStartInfo start, MeasurementLimits limits, CancellationToken cancellation = default)
    {
        if (limits.Deadline <= TimeSpan.Zero || limits.Deadline.TotalMilliseconds >= uint.MaxValue || limits.StdoutBytes <= 0 || limits.StderrBytes <= 0) throw new ArgumentException("Positive process deadline (below 49 days) and output budgets required");
        cancellation.ThrowIfCancellationRequested();
        start.UseShellExecute = false; start.CreateNoWindow = true; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(limits.Deadline);
        var timer = Stopwatch.StartNew();
        var process = Process.Start(start) ?? throw new IOException("Measurement child failed to start");
        var stdout = Read(process.StandardOutput.BaseStream, limits.StdoutBytes, deadline);
        var stderr = Read(process.StandardError.BaseStream, limits.StderrBytes, deadline);
        ProcessObservation? result = null; Exception? failure = null; long? peak = null; double? cpu = null;
        try
        {
            while (!process.HasExited)
            {
                if (stdout.IsFaulted) await stdout;
                if (stderr.IsFaulted) await stderr;
                process.Refresh();
                try { var sample = process.PeakWorkingSet64; if (sample > 0) peak = Math.Max(peak ?? 0, sample); cpu = process.TotalProcessorTime.TotalMilliseconds; }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException) { }
                await Task.Delay(5, deadline.Token);
            }
            await process.WaitForExitAsync(deadline.Token); timer.Stop();
            try { cpu = process.TotalProcessorTime.TotalMilliseconds; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException) { }
            result = new(process.ExitCode, await stdout, await stderr, timer.Elapsed.TotalMilliseconds, cpu, peak);
        }
        catch (Exception ex) { failure = ex; }
        var exited = false;
        try
        {
            deadline.Cancel();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(cleanup.Token); exited = process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
        { failure = failure is null ? ex : new AggregateException(failure, ex); }
        foreach (var task in new[] { stdout, stderr })
        {
            try { await task.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or OperationCanceledException or TimeoutException) { failure ??= ex; }
        }
        if (exited) process.Dispose();
        if (failure is not null || !exited)
        {
            var capacity = new[] { stdout, stderr }.Any(task => task.Exception?.GetBaseException() is InvalidDataException);
            var reason = !exited ? "Measurement cleanup failed; retained process owner" : capacity ? "Measurement output capacity exceeded" : cancellation.IsCancellationRequested ? "Measurement cancelled" : failure is OperationCanceledException ? "Measurement deadline exceeded" : "Measurement process failed";
            throw new MeasurementProcessException(reason, exited, failure ?? new IOException("Child exit unverified"), exited ? null : process);
        }
        return result ?? throw new InvalidDataException("Missing process observation");
    }

    private static async Task<byte[]> Read(Stream stream, int limit, CancellationTokenSource cancellation)
    {
        using var output = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellation.Token);
            if (count == 0) return output.ToArray();
            if (output.Length + count > limit) { cancellation.Cancel(); throw new InvalidDataException("Output budget exceeded"); }
            output.Write(buffer, 0, count);
        }
    }
}
