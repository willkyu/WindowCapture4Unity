using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace WindowCapture
{
    internal static class UnityMainThread
    {
        private static readonly object Sync = new object();
        private static SynchronizationContext context;
        private static int mainThreadId;

        public static bool IsInitialized => context != null;

        public static bool IsMainThread =>
            context != null && Thread.CurrentThread.ManagedThreadId == mainThreadId;

        public static void InitializeFromCurrentThreadIfNeeded()
        {
            if (IsInitialized)
                return;

            SynchronizationContext current = SynchronizationContext.Current;
            if (current != null)
                Initialize(current);
        }

        public static void Initialize(SynchronizationContext synchronizationContext)
        {
            if (synchronizationContext == null)
                throw new ArgumentNullException(nameof(synchronizationContext));

            lock (Sync)
            {
                context = synchronizationContext;
                mainThreadId = Thread.CurrentThread.ManagedThreadId;
            }
        }

        public static void Invoke(Action action)
        {
            if (action == null)
                return;

            if (IsMainThread)
            {
                action();
                return;
            }

            SynchronizationContext target = context;
            if (target == null)
                throw new InvalidOperationException("Unity main-thread dispatcher is not initialized. Create or initialize this source on the Unity main thread first.");

            ExceptionDispatchInfo captured = null;
            using (var done = new ManualResetEventSlim(false))
            {
                target.Post(_ =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        captured = ExceptionDispatchInfo.Capture(ex);
                    }
                    finally
                    {
                        done.Set();
                    }
                }, null);

                done.Wait();
            }

            captured?.Throw();
        }
    }
}
