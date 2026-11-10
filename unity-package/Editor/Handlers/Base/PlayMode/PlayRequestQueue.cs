using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Tasks;
using UnityEditor;

namespace UnityMcp.Editor
{
    /// <summary>Separate from asset-edit jobs; continuations resume on Unity's main-thread context.</summary>
    [InitializeOnLoad]
    internal static class PlayRequestQueue
    {
        sealed class Job
        {
            public Func<Task<string>> Execute;
            public readonly TaskCompletionSource<string> Completion = new TaskCompletionSource<string>();
            public volatile bool Cancelled;
        }
        static readonly ConcurrentQueue<Job> queue = new ConcurrentQueue<Job>();
        static Job active;
        static int count;
        static bool stopped;
        static PlayRequestQueue()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }
        internal static void Initialize() { }
        static void Stop()
        {
            stopped = true;
            if (active != null) { active.Cancelled = true; active.Completion.TrySetCanceled(); }
            while (queue.TryDequeue(out var job)) { System.Threading.Interlocked.Decrement(ref count); job.Completion.TrySetCanceled(); }
        }
        static void Update()
        {
            if (stopped || active != null || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (queue.TryDequeue(out var job))
            {
                System.Threading.Interlocked.Decrement(ref count);
                if (!job.Cancelled) Run(job);
            }
        }
        static async void Run(Job job)
        {
            active = job;
            try { job.Completion.TrySetResult(await job.Execute()); }
            catch (Exception error) { job.Completion.TrySetException(error); }
            finally { active = null; }
        }
        internal static async Task Handle<T>(HttpListenerContext context, Action<T> validate,
            Func<T, Task<string>> execute, Func<string, string, string> error) where T : class, IEditorRequest
        {
            T request = null;
            Job job = null;
            try
            {
                request = await HttpRequestUtility.Read<T>(context).ConfigureAwait(false);
                HttpRequestUtility.ValidateEnvelope(request);
                validate(request);
                if (System.Threading.Interlocked.Increment(ref count) > 16)
                {
                    System.Threading.Interlocked.Decrement(ref count);
                    throw new Exception("Play request queue full");
                }
                job = new Job { Execute = async () => { HttpRequestUtility.ValidateEnvelope(request); return await execute(request); } };
                queue.Enqueue(job);
                if (await Task.WhenAny(job.Completion.Task, Task.Delay(20000)).ConfigureAwait(false) != job.Completion.Task)
                { job.Cancelled = true; throw new Exception("Play request timed out; inspect runtime state before retrying"); }
                await EditorHttpResponse.Send(context, 200, await job.Completion.Task.ConfigureAwait(false)).ConfigureAwait(false);
            }
            catch (HttpRequestBodyReader.ReadException e) { await EditorHttpResponse.Send(context, e.StatusCode, e.ResponseJson).ConfigureAwait(false); }
            catch (Exception e) { await EditorHttpResponse.Send(context, 200, error(request?.Id, e.Message)).ConfigureAwait(false); }
        }
    }
}
