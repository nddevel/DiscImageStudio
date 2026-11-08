namespace DiscImageStudio.Services;

/// <summary>Linux counterpart of the WPF app's STA worker. There is no COM apartment to
/// enter, so long-running image work simply gets its own background thread.</summary>
internal static class StaWorker
{
    internal static Task<T> RunAsync<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Disc image worker",
        };
        thread.Start();
        return completion.Task;
    }

    internal static Task RunAsync(Action action) => RunAsync(() =>
    {
        action();
        return true;
    });
}
