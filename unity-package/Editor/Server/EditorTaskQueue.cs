using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace UnityMcp.Editor
{
    /// <summary>Owns pending tasks, capacity reservations and shutdown cancellation.</summary>
    internal sealed class EditorTaskQueue
    {
        internal sealed class Pending
        {
            public readonly Func<string> Execute;
            public Pending(Func<string> execute)
            {
                Execute = execute ?? throw new ArgumentNullException(nameof(execute));
            }
            int cancelled;
            public bool IsCancelled => Volatile.Read(ref cancelled) != 0;
            public void Cancel() { Interlocked.Exchange(ref cancelled, 1); }
            public readonly TaskCompletionSource<string> Completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        const int Capacity = 32;
        readonly ConcurrentQueue<Pending> jobs = new ConcurrentQueue<Pending>();
        readonly object gate = new object();
        int pendingCount;
        bool stopped;

        public async Task EnqueueAndRespond(HttpListenerContext context, Func<string> execute)
        {
            bool counted = false;
            Pending job = null;
            try
            {
                if (!TryReserveRequest())
                { await EditorHttpResponse.Send(context, 429, "{\"error\":\"Bridge queue full\"}"); return; }
                counted = true;
                job = new Pending(execute);
                if (!TryEnqueue(job))
                { await EditorHttpResponse.Send(context, 503, "{\"error\":\"Bridge stopped\"}"); return; }
                counted = false; // Queue now owns this admission slot.
                if (await Task.WhenAny(job.Completion.Task, Task.Delay(20000)).ConfigureAwait(false) != job.Completion.Task)
                {
                    job.Cancel();
                    await EditorHttpResponse.Send(context, 504, "{\"error\":\"Editor request timed out; inspect output before retrying\"}");
                    return;
                }
                await EditorHttpResponse.Send(context, 200, await job.Completion.Task.ConfigureAwait(false));
            }
            catch (Exception)
            {
                if (job != null) job.Cancel();
                await EditorHttpResponse.Send(context, 500, "{\"error\":\"Bridge transport failure\"}");
            }
            finally { if (counted) ReleaseRequest(); }
        }

        // Reserves capacity after request parsing, before enqueueing.
        public bool TryReserveRequest()
        {
            lock (gate)
            {
                if (stopped) return false;
                if (Interlocked.Increment(ref pendingCount) <= Capacity) return true;
                Interlocked.Decrement(ref pendingCount);
                return false;
            }
        }
        public void ReleaseRequest()
        {
            Interlocked.Decrement(ref pendingCount);
        }
        // Transfer an existing reservation to the queue only when this succeeds.
        public bool TryEnqueue(Pending job)
        {
            lock (gate)
            {
                if (stopped) return false;
                jobs.Enqueue(job);
                return true;
            }
        }
        public bool TryPeek(out Pending job)
        {
            return jobs.TryPeek(out job);
        }
        public bool TryDequeue(out Pending job)
        {
            lock (gate)
            {
                if (!jobs.TryDequeue(out job)) return false;
                ReleaseRequest();
                return true;
            }
        }
        public void DiscardCancelled()
        {
            lock (gate)
            {
                while (jobs.TryPeek(out var job) && job.IsCancelled)
                    TryDequeue(out _);
            }
        }
        // Called on the main thread; the queue decides whether Unity can execute tasks.
        public void Update()
        {
            if (stopped) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode) return;
            DiscardCancelled();
            if (!TryDequeue(out var job)) return;
            if (job.IsCancelled)
            {
                job.Completion.TrySetCanceled();
                return;
            }
            try { job.Completion.TrySetResult(job.Execute()); }
            catch (Exception e) { job.Completion.TrySetException(e); }
        }
        public void Stop()
        {
            lock (gate)
            {
                stopped = true;
                while (TryDequeue(out var job))
                {
                    job.Cancel();
                    job.Completion.TrySetCanceled();
                }
            }
        }
    }
}
