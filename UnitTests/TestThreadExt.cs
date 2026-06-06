using System;
using System.Threading;
using Ship_Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTests
{
    [TestClass]
    public class TestThreadExt : StarDriveTest
    {
        [TestMethod]
        public void TestParallelFor()
        {
            var numbers = new int[13333337];
            Parallel.For(0, numbers.Length, (start, end) =>
            {
                for (int i = start; i < end; ++i)
                    numbers[i] = i;
            });

            var timer = new PerfTimer();
            long sum = 0;
            Parallel.For(0, numbers.Length, (start, end) =>
            {
                long isum = 0;
                for (int i = start; i < end; ++i)
                    isum += (long)Math.Sqrt(numbers[i]);
                Interlocked.Add(ref sum, isum);
            });
            Console.WriteLine("ParallelFor  elapsed: {0:0.000}s  result: {1}", timer.Elapsed, sum);

            timer.Start();
            long sum2 = 0;
            foreach (int value in numbers)
                sum2 += (long)Math.Sqrt(value);
            Console.WriteLine("SingleThread elapsed: {0:0.000}s  result: {1}", timer.Elapsed, sum2);

            AssertEqual(sum2, sum, "Parallel.For result incorrect. Incorrect loop logic?");

            // Test the parallel loop a second time to ensure it doesn't deadlock etc
            int poolSize = Parallel.PoolSize;
            timer.Start();
            long sum3 = 0;
            Parallel.For(0, numbers.Length, (start, end) =>
            {
                long isum = 0;
                for (int i = start; i < end; ++i)
                    isum += (long)Math.Sqrt(numbers[i]);
                Interlocked.Add(ref sum3, isum);
            });
            Console.WriteLine("ParallelFor  elapsed: {0:0.000}s  result: {1}", timer.Elapsed, sum3);

            numbers = null;
            GC.Collect(); // Fixes Test OOM in Debug mode

            AssertEqual(sum2, sum3, "Parallel.For result incorrect. Incorrect loop logic?");
            AssertEqual(poolSize, Parallel.PoolSize, "Parallel.For pool is growing, but it shouldn't. Incorrect ParallelTask states?");
        }

        [TestMethod]
        public void TestAllowConcurrentPForLoops()
        {
            // These PFor loops are unrelated to one another, thus they should
            // run without throwing ThreadStateException
            var items = new int[1337];
            Parallel.Run(() =>
            {
                Parallel.For(0, items.Length, (start, end) =>
                {
                    Thread.Sleep(1);
                });
            });
            Parallel.Run(() =>
            {
                Parallel.For(0, items.Length, (start, end) =>
                {
                    Thread.Sleep(1);
                });
            });
            Parallel.Run(() =>
            {
                Parallel.For(0, items.Length, (start, end) =>
                {
                    Thread.Sleep(1);
                });
            });
        }

        // Reproduces the Sentry crash where the OS refused to create a worker thread under
        // memory pressure (OutOfMemoryException from Thread.Start in ParallelTask). The engine
        // must degrade gracefully: run the queued task synchronously on the calling thread
        // instead of letting the OOM crash the sim. TestForceThreadStartFailure simulates the
        // thread-creation failure deterministically.
        [TestMethod]
        public void TestInlineFallbackWhenThreadStartFails()
        {
            Parallel.ClearPool(); // cold pool: freshly spawned tasks have an unstarted thread, so
                                  // TriggerTaskStart hits the (simulated) start-failure path.
            int baseline = Parallel.InlineFallbackCount;
            int callerThreadId = Thread.CurrentThread.ManagedThreadId;
            Parallel.TestForceThreadStartFailure = true;
            try
            {
                // VoidTask path: Parallel.Run always dispatches through the pool/thread-start,
                // regardless of core count, so it deterministically exercises the fallback.
                bool ranVoid = false;
                int voidThreadId = -1;
                Parallel.Run(() => { ranVoid = true; voidThreadId = Thread.CurrentThread.ManagedThreadId; }).Wait();
                Assert.IsTrue(ranVoid, "Run(Action) body must still execute when the worker thread can't start");
                AssertEqual(callerThreadId, voidThreadId, "Fallback must run the task inline on the calling thread");

                // Fire-and-forget: a Run whose result is never waited on must STILL execute, because
                // the fallback runs inline at scheduling time (after lock (Pool) is released) - not
                // deferred to a Wait() that may never come. Regression guard for the unlocked drain.
                bool ranForget = false;
                int forgetThreadId = -1;
                Parallel.Run(() => { ranForget = true; forgetThreadId = Thread.CurrentThread.ManagedThreadId; });
                Assert.IsTrue(ranForget, "Fire-and-forget Run(Action) must execute inline even without Wait()");
                AssertEqual(callerThreadId, forgetThreadId, "Deferred fallback must run on the scheduling thread");

                // ResultTask path: the computed value must come back through the inline fallback.
                var typed = Parallel.Run(() => 6 * 7);
                typed.Wait();
                AssertEqual(42, typed.Result, "Run(Func) must return the result computed inline");

                // Exceptions from the body must still surface (not be swallowed by the fallback).
                var faulted = Parallel.Run((Action)(() => throw new ArgumentException("boom")));
                var ex = Assert.ThrowsExactly<ParallelTaskException>(() => faulted.Wait());
                AssertEqual("boom", ex.InnerException?.Message);

                Assert.IsTrue(Parallel.InlineFallbackCount > baseline,
                    "Inline fallback counter should increment while thread-start is forced to fail");
            }
            finally
            {
                Parallel.TestForceThreadStartFailure = false;
                Parallel.ClearPool(); // discard tasks left thread-less by the fallback
            }
        }

        [TestMethod]
        public void TestPForExceptions()
        {
            void Action()
            {
                var items = new int[1337];
                Parallel.For(0, items.Length, (start, end) => throw new ArgumentException("Test"), maxParallelism:4);
            }

            Log.Write($"Parallel.MaxParallelism: {Parallel.MaxParallelism}");
            Log.Write($"Parallel.NumPhysicalCores: {Parallel.NumPhysicalCores}");

            // AppVeyor CI quite often runs 1-core only, which makes Parallel tests most vexing
            if (Parallel.MaxParallelism == 1)
            {
                var ex = Assert.ThrowsExactly<ArgumentException>((Action)Action);
                AssertEqual("Test", ex.Message);
            }
            else
            {
                var ex = Assert.ThrowsExactly<ParallelTaskException>((Action)Action);
                AssertEqual(typeof(ArgumentException), ex.InnerException?.GetType());
                AssertEqual("Test", ex.InnerException?.Message);
                AssertEqual("Parallel.For task threw an exception", ex.Message);
            }
        }
    }
}
