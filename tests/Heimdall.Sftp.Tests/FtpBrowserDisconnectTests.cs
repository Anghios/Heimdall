/*
 * Copyright 2026 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Diagnostics;
using System.Reflection;

namespace Heimdall.Sftp.Tests;

public sealed class FtpBrowserDisconnectTests
{
    /// <summary>
    /// Failure bound, not a synchronisation point: the waits it bounds complete when the lock is
    /// released or Disconnect returns, and it is paid only on failure.
    /// </summary>
    private static readonly TimeSpan SignalBackstop = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Disconnect's own bound in the test that holds the lock. Longer than the backstop, so a
    /// Disconnect that returns within the backstop after the release can only have taken the lock.
    /// </summary>
    private static readonly TimeSpan HeldLockDisconnectBound = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long the lock is held before it is released. A window, not a bound: stretching it under
    /// load cannot turn a correct Disconnect red, only give a wrong one longer to show itself.
    /// </summary>
    private static readonly TimeSpan HeldLockObservationWindow = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// How long a Disconnect that cannot take the lock may run under the production bound of 250
    /// milliseconds. The rest is scheduling margin: a CPU saturated by sixteen busy processes and
    /// a solution build stretched Disconnect to 1.25 seconds at most over 200 runs.
    /// </summary>
    private static readonly TimeSpan HeldLockDisconnectReturnBound = TimeSpan.FromSeconds(5);

    /// <remarks>
    /// This used to run against the production bound of 250 milliseconds and assert that Disconnect
    /// had not returned after 100. Two clocks raced: the 250 millisecond wait is a kernel timeout
    /// that needs no pool thread once Disconnect is running, while the 100 millisecond delay
    /// completes only through a timer callback on the thread pool. A pool starved for longer than
    /// the gap let Disconnect give up on the lock and return first. It failed that way once in a
    /// full-solution run, and a loop over the same body with the pool blocked by sleeping work items
    /// failed 15 times in 30, always on that assertion with Disconnect returning at its bound.
    /// </remarks>
    [Fact]
    public async Task Disconnect_WhenOperationIsActive_WaitsForTheLock()
    {
        using FtpBrowser browser = new(HeldLockDisconnectBound);
        SemaphoreSlim operationLock = GetOperationLock(browser);
        await operationLock.WaitAsync();

        Task disconnect = Task.Run(browser.Disconnect);
        try
        {
            Task finished = await Task.WhenAny(disconnect, Task.Delay(HeldLockObservationWindow));
            Assert.NotSame(disconnect, finished);
        }
        finally
        {
            operationLock.Release();
        }

        await disconnect.WaitAsync(SignalBackstop);
    }

    [Fact]
    public async Task DisconnectAsync_WhenOperationIsActive_WaitsWithoutBlockingCaller()
    {
        using FtpBrowser browser = new();
        SemaphoreSlim operationLock = GetOperationLock(browser);
        await operationLock.WaitAsync();

        try
        {
            Task disconnect = browser.DisconnectAsync(CancellationToken.None);

            Assert.False(disconnect.IsCompleted);

            operationLock.Release();
            await disconnect.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            if (operationLock.CurrentCount == 0)
            {
                operationLock.Release();
            }
        }
    }

    [Fact]
    public async Task DisconnectAsync_WhenOperationIsActive_HonorsCancellation()
    {
        using FtpBrowser browser = new();
        SemaphoreSlim operationLock = GetOperationLock(browser);
        await operationLock.WaitAsync();
        using CancellationTokenSource cancellation = new();

        try
        {
            Task disconnect = browser.DisconnectAsync(cancellation.Token);

            Assert.False(disconnect.IsCompleted);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => disconnect);
        }
        finally
        {
            operationLock.Release();
        }
    }

    /// <remarks>
    /// The synchronous Disconnect waited on the operation lock with no timeout, pinning a
    /// thread for the whole of a stalled transfer, and Dispose then disposed the semaphore
    /// under any waiter - the opposite of the decision the SFTP browser pins by test.
    /// <para>
    /// Disconnect runs on a thread of its own and is awaited by Join, so neither its start nor
    /// the bound depends on the thread pool. It used to be started by Task.Run and raced against
    /// Task.Delay: both need a pool thread, and a starved pool spent the bound before Disconnect
    /// ran at all. It failed that way once in a full-solution run on a loaded machine. Replaying
    /// that body with the pool blocked by sleeping work items, Disconnect started up to 13.8
    /// seconds late while its own run never exceeded 267 milliseconds.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Disconnect_WhenOperationIsActive_ReturnsWithinItsBound()
    {
        using FtpBrowser browser = new();
        SemaphoreSlim operationLock = GetOperationLock(browser);
        await operationLock.WaitAsync();

        Exception? failure = null;
        long startedAt = 0;
        Thread disconnect = new(() =>
        {
            Interlocked.Exchange(ref startedAt, Stopwatch.GetTimestamp());
            try
            {
                browser.Disconnect();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
        };

        try
        {
            long launchedAt = Stopwatch.GetTimestamp();
            disconnect.Start();
            bool returned = disconnect.Join(HeldLockDisconnectReturnBound);

            long started = Interlocked.Read(ref startedAt);
            Assert.True(
                returned,
                $"Disconnect did not return within {HeldLockDisconnectReturnBound.TotalMilliseconds} ms "
                + $"while the lock was held; its thread started "
                + (started == 0
                    ? "never"
                    : $"{Stopwatch.GetElapsedTime(launchedAt, started).TotalMilliseconds:F0} ms after launch")
                + ".");
            Assert.Null(failure);
        }
        finally
        {
            operationLock.Release();
        }
    }

    [Fact]
    public async Task Dispose_DoesNotDisposeTheOperationLock()
    {
        FtpBrowser browser = new();
        SemaphoreSlim operationLock = GetOperationLock(browser);

        browser.Dispose();

        // A waiter arriving after the teardown meets the lock, not an ObjectDisposedException.
        await operationLock.WaitAsync();
        operationLock.Release();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("///")]
    public async Task DeleteAsync_RejectsProtectedRootBeforeConnectionCheck(string path)
    {
        using FtpBrowser browser = new();

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => browser.DeleteAsync(path));

        Assert.Contains("protected remote root", refusal.Message, StringComparison.Ordinal);
    }

    private static SemaphoreSlim GetOperationLock(FtpBrowser browser)
    {
        FieldInfo field = typeof(FtpBrowser).GetField(
            "_opLock",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FtpBrowser operation lock was not found.");

        return (SemaphoreSlim)(field.GetValue(browser)
            ?? throw new InvalidOperationException("FtpBrowser operation lock was null."));
    }
}
