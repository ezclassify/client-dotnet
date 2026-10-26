using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace EzClassify;

/// <summary>
/// The one deadline and cancellation boundary a request runs under.
///
/// <para>It opens before the request is built and closes when the request
/// settles, so sending, waiting for the response and reading the whole body
/// all draw on the same deadline. A caller's token that fires first still
/// wins. A request therefore never waits longer than the client's timeout,
/// whatever the transport underneath does.</para>
///
/// <para>Cancellation alone cannot promise that: a handler or body stream that
/// ignores its token would keep the request waiting forever. <see cref="RunAsync{T}"/>
/// stops waiting for such work once the budget is spent and leaves it to
/// settle on its own, disposing whatever it eventually returns.</para>
///
/// <para>Disposing the budget releases its timer and its registration on the
/// caller's token, so a caller that reuses one token across many requests
/// does not collect a registration per call.</para>
/// </summary>
internal sealed class RequestBudget : IDisposable
{
    private readonly string _path;
    private readonly TimeSpan _timeout;
    private readonly CancellationToken _caller;
    private readonly CancellationTokenSource _linked;
    private readonly CancellationTokenRegistration _spent;
    private volatile bool _timedOut;
    private readonly long _started = Stopwatch.GetTimestamp();

    // Timers fire on a coarse clock, so a transport timer armed for the same
    // duration can fire a little before or after this budget's. A cancellation
    // this close to the deadline is the deadline.
    private static readonly TimeSpan DeadlineSlack = TimeSpan.FromMilliseconds(100);

    /// <param name="path">Request path, named in the timeout error. A query string is left out of the message.</param>
    /// <param name="timeout">The client's request timeout; positive and finite.</param>
    /// <param name="caller">The caller's token, or a shared cache entry's.</param>
    internal RequestBudget(string path, TimeSpan timeout, CancellationToken caller)
    {
        var query = path.IndexOf('?');
        _path = query < 0 ? path : path.Substring(0, query);
        _timeout = timeout;
        _caller = caller;
        _linked = CancellationTokenSource.CreateLinkedTokenSource(caller);
        // Whatever cancels the linked token without the caller having canceled
        // is the deadline. A caller that canceled first keeps its cancellation.
        _spent = _linked.Token.Register(
            static state =>
            {
                var budget = (RequestBudget)state!;
                if (!budget._caller.IsCancellationRequested) budget._timedOut = true;
            },
            this);
        _linked.CancelAfter(timeout);
    }

    /// <summary>Canceled when the deadline passes or the caller cancels.</summary>
    internal CancellationToken Token => _linked.Token;

    /// <summary>
    /// Run one stage of the request, waiting no longer than the budget allows.
    /// </summary>
    /// <param name="start">Starts the work with <see cref="Token"/>; not called once the budget is spent.</param>
    /// <returns>The work's result.</returns>
    /// <exception cref="OperationCanceledException">The caller canceled.</exception>
    /// <exception cref="EzClassifyClientException">The deadline passed; <see cref="Exception.InnerException"/> is a <see cref="TimeoutException"/>.</exception>
    internal async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> start)
    {
        if (Token.IsCancellationRequested) throw Spent(null);
        Task<T> work;
        try
        {
            work = start(Token);
        }
        catch (OperationCanceledException canceled)
        {
            throw Spent(canceled);
        }

        if (!work.IsCompleted)
        {
            var spent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (Token.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), spent))
                await Task.WhenAny(work, spent.Task).ConfigureAwait(false);
            if (!work.IsCompleted)
            {
                Abandon(work);
                throw Spent(null);
            }
        }

        try
        {
            return await work.ConfigureAwait(false);
        }
        catch (OperationCanceledException canceled)
        {
            // This budget's token, or HttpClient's own timeout, which it
            // reports as a cancellation nobody asked for.
            throw Spent(canceled);
        }
    }

    /// <summary>
    /// The error a spent budget reports. The caller's own cancellation stays a
    /// cancellation; an elapsed deadline, this budget's or the transport's, is
    /// a timeout. The transport's own exception, when there is one, stays as
    /// the inner exception.
    /// </summary>
    private Exception Spent(Exception? cause)
    {
        if (!_timedOut && _caller.IsCancellationRequested)
            return new OperationCanceledException(cause?.Message ?? "The operation was canceled.", cause, _caller);
        // A transport that cancels on its own, such as a per-try timeout in a
        // resilience handler, can end the request well before this deadline;
        // the message says so rather than naming a deadline that has not
        // passed. The token, not _timedOut, marks this budget's own deadline:
        // it is canceled before any callback runs, so the transport's reaction
        // to it can arrive before the callback that sets _timedOut. HttpClient's
        // own timeout, set to the same duration, lands within the slack.
        var milliseconds = (long)_timeout.TotalMilliseconds;
        var message = Token.IsCancellationRequested || cause is null || Elapsed >= _timeout - DeadlineSlack
            ? $"Request to {_path} timed out after {milliseconds} ms"
            : $"Request to {_path} was canceled by the transport before the {milliseconds} ms deadline";
        return new EzClassifyClientException(message, new TimeoutException(message, cause));
    }

    private TimeSpan Elapsed =>
        TimeSpan.FromTicks((Stopwatch.GetTimestamp() - _started) * TimeSpan.TicksPerSecond / Stopwatch.Frequency);

    // Nothing waits for abandoned work any more. Observe its failure so it is
    // not reported as unobserved, and dispose a response that arrives late.
    private static void Abandon<T>(Task<T> work) =>
        _ = work.ContinueWith(
            static settled =>
            {
                if (settled.Status == TaskStatus.RanToCompletion)
                    (settled.Result as IDisposable)?.Dispose();
                else
                    _ = settled.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    public void Dispose()
    {
        _spent.Dispose();
        _linked.Dispose();
    }
}
