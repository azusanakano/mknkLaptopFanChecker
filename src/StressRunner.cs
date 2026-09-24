using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Mknk.LaptopFanChecker
{
    public sealed class CpuStressRunner : IDisposable
    {
        private readonly object _sync = new object();
        private CancellationTokenSource _cancellation;
        private List<Task> _workers;
        private static double _sink;
        public int WorkerCount { get; private set; }

        public bool IsRunning
        {
            get
            {
                lock (_sync)
                    return _cancellation != null;
            }
        }

        public void Start(int targetPercent, int maximumSeconds)
        {
            lock (_sync)
            {
                StopInternal();
                int duty = Math.Max(20, Math.Min(95, targetPercent));
                _cancellation = new CancellationTokenSource();
                _workers = new List<Task>();
                int count = Math.Max(1, Environment.ProcessorCount);
                WorkerCount = count;
                int duration = Math.Max(1, Math.Min(300, maximumSeconds));
                for (int i = 0; i < count; i++)
                {
                    CancellationToken token = _cancellation.Token;
                    Task worker = Task.Factory.StartNew(
                        delegate { WorkerLoop(token, duty, duration); },
                        token,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default);
                    _workers.Add(worker);
                }
            }
        }

        public void Stop()
        {
            lock (_sync)
                StopInternal();
        }

        public void Dispose()
        {
            Stop();
        }

        private void StopInternal()
        {
            if (_cancellation == null)
                return;
            try { _cancellation.Cancel(); }
            catch { }
            if (_workers != null)
            {
                try { Task.WaitAll(_workers.ToArray(), 800); }
                catch { }
            }
            _cancellation.Dispose();
            _cancellation = null;
            _workers = null;
        }

        private static void WorkerLoop(CancellationToken token, int dutyPercent, int maximumSeconds)
        {
            try { Thread.CurrentThread.Priority = ThreadPriority.BelowNormal; }
            catch { }

            const int cycleMilliseconds = 100;
            int busyMilliseconds = Math.Max(1, cycleMilliseconds * dutyPercent / 100);
            int restMilliseconds = Math.Max(1, cycleMilliseconds - busyMilliseconds);
            double value = 0.731;

            Stopwatch lifetime = Stopwatch.StartNew();
            while (!token.IsCancellationRequested && lifetime.Elapsed.TotalSeconds < maximumSeconds)
            {
                Stopwatch watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < busyMilliseconds && !token.IsCancellationRequested)
                {
                    for (int i = 1; i < 3500; i++)
                        value = Math.Sqrt(value * value + i) % 997.0;
                }
                Interlocked.Exchange(ref _sink, value);
                if (!token.IsCancellationRequested)
                    Thread.Sleep(restMilliseconds);
            }
        }
    }
}
