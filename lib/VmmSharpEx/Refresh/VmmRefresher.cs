/*  
 *  VmmSharpEx by Lone (Lone DMA)
 *  Copyright (C) 2025 AGPL-3.0
*/

using VmmSharpEx;
using VmmSharpEx.Options;
using VmmSharpEx.Refresh;

internal sealed class VmmRefresher : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    private VmmRefresher() { }

    public VmmRefresher(Vmm instance, RefreshOption option, TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero, nameof(interval));
        _ = Task.Run(() => RunAsync(instance, option, interval, _cts.Token));
    }

    private static async Task RunAsync(Vmm instance, RefreshOption option, TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                while (!instance.IsDisposed && await timer.WaitForNextTickAsync(ct))
                {
                    if (!instance.ConfigSet((VmmOption)option, 1))
                        instance.Log($"WARNING: {option} Auto Refresh Failed!", Vmm.LogLevel.Warning);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true) == false)
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}