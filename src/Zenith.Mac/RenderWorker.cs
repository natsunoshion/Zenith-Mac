using System.Collections.Concurrent;
using Zenith.Core.Rendering;

namespace Zenith.Mac;
internal sealed class RenderWorker : IDisposable
{
    readonly BlockingCollection<Action> work = new();
    readonly Thread thread;
    readonly object lifecycle = new();
    SceneRenderer? renderer;
    bool disposed;
    public RenderWorker(Func<SceneRenderer> factory)
    {
        thread = new Thread(() =>
        {
            foreach (var action in work.GetConsumingEnumerable())
                action();
        })
        {
            IsBackground = true,
            Name = "Zenith GPU"
        };
        thread.Start();
        try
        {
            Invoke(() =>
            {
                renderer = factory();
                return 0;
            }).GetAwaiter().GetResult();
        }
        catch
        {
            work.CompleteAdding();
            thread.Join();
            work.Dispose();
            throw;
        }
    }

    Task<T> Invoke<T>(Func<T> action)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return Enqueue(action);
        }
    }

    Task<T> Enqueue<T>(Func<T> action)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Add(() =>
        {
            try
            {
                done.SetResult(action());
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        });
        return done.Task;
    }

    public Task<T> Run<T>(Func<SceneRenderer, T> action) => Invoke(() => action(renderer
        ?? throw new InvalidOperationException("The render session is unavailable after a failed reset.")));
    public Task Reset(Func<SceneRenderer> factory) => Invoke(() =>
    {
        var previous = renderer;
        renderer = null;
        previous?.Dispose();
        renderer = factory();
        return 0;
    });
    public void Dispose()
    {
        Task cleanup;
        lock (lifecycle)
        {
            if (disposed) return;
            disposed = true;
            cleanup = Enqueue(() =>
            {
                var previous = renderer;
                renderer = null;
                previous?.Dispose();
                return 0;
            });
            work.CompleteAdding();
        }
        try { cleanup.GetAwaiter().GetResult(); }
        finally
        {
            // A plugin disposal error must not leave the GPU thread blocked on
            // its queue, or leak the queue's native synchronization resources.
            thread.Join();
            work.Dispose();
        }
    }
}
