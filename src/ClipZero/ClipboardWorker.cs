namespace ClipZero;

/// <summary>Clipboard providers may render data synchronously. Keep that work off the UI thread.</summary>
internal sealed class ClipboardWorker : IDisposable
{
    private readonly Thread _thread;
    private readonly TaskCompletionSource<Control> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;

    internal ClipboardWorker()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "ClipZero clipboard STA" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Run()
    {
        try
        {
            using var dispatcher = new Control();
            _ = dispatcher.Handle;
            using var context = new ApplicationContext();
            _ready.SetResult(dispatcher);
            Application.Run(context);
        }
        catch (Exception exception) { _ready.TrySetException(exception); }
    }

    internal async Task<T> InvokeAsync<T>(Func<nint, T> operation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Control dispatcher = await _ready.Task;
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.BeginInvoke((Action)(() =>
        {
            try { result.SetResult(operation(dispatcher.Handle)); }
            catch (Exception exception) { result.SetException(exception); }
        }));
        return await result.Task;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ready.Task.IsCompletedSuccessfully)
        {
            try { _ready.Task.Result.BeginInvoke((Action)Application.ExitThread); }
            catch (InvalidOperationException) { }
        }
        // Do not hang exit if a third-party clipboard owner is unresponsive.
        _thread.Join(200);
    }
}
