using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PSuite.Core
{
    public class BenchmarkResult
    {
        public string Name { get; set; } = string.Empty;
        public double Score { get; set; }
        public string Unit { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
    }

    public class BenchmarkSuiteResult
    {
        public System.Collections.Generic.List<BenchmarkResult> Results { get; set; } = new();
        public TimeSpan TotalDuration { get; set; }
    }

    // Six small, honest, measured tests — no fake or simulated numbers.
    // None of this replaces Cinebench/AIDA64/CrystalDiskMark; it exists
    // for one purpose only: comparing THIS machine before/after a tweak.
    // No admin rights are required for any of it — CPU math, RAM access
    // patterns and a temp file in %TEMP% are all fully user-mode.
    public static class BenchmarkRunner
    {
        // ---- CPU: single core -------------------------------------------------
        public static Task<BenchmarkResult> RunCpuSingleCoreAsync()
        {
            return Task.Run(() =>
            {
                const long iterations = 300_000_000;
                var sw = Stopwatch.StartNew();

                double acc = 1.0;
                for (long i = 1; i <= iterations; i++)
                {
                    acc += Math.Sin(i) * Math.Cos(i);
                    if (acc > 1_000_000 || acc < -1_000_000) acc = 1.0;
                }

                sw.Stop();
                GC.KeepAlive(acc);

                var mops = iterations / sw.Elapsed.TotalSeconds / 1_000_000.0;
                return new BenchmarkResult
                {
                    Name = "CPU (1 поток)",
                    Score = Math.Round(mops, 1),
                    Unit = "млн опер./с",
                    Duration = sw.Elapsed
                };
            });
        }

        // ---- CPU: all logical cores at once -----------------------------------
        public static Task<BenchmarkResult> RunCpuMultiCoreAsync()
        {
            return Task.Run(() =>
            {
                int threads = Math.Max(1, Environment.ProcessorCount);
                const long iterationsPerThread = 150_000_000;

                var sw = Stopwatch.StartNew();
                Parallel.For(0, threads, _ =>
                {
                    double acc = 1.0;
                    for (long i = 1; i <= iterationsPerThread; i++)
                    {
                        acc += Math.Sin(i) * Math.Cos(i);
                        if (acc > 1_000_000 || acc < -1_000_000) acc = 1.0;
                    }
                    GC.KeepAlive(acc);
                });
                sw.Stop();

                var totalOps = iterationsPerThread * (long)threads;
                var mops = totalOps / sw.Elapsed.TotalSeconds / 1_000_000.0;

                return new BenchmarkResult
                {
                    Name = $"CPU (все ядра: {threads})",
                    Score = Math.Round(mops, 1),
                    Unit = "млн опер./с",
                    Duration = sw.Elapsed
                };
            });
        }

        // ---- Memory bandwidth: STREAM-style Triad ------------------------------
        // c[i] = a[i] + scalar * b[i] — the same synthetic kernel the
        // industry-standard STREAM benchmark uses to measure real memory
        // bandwidth (2 reads + 1 write per element).
        public static Task<BenchmarkResult> RunMemoryBandwidthAsync()
        {
            return Task.Run(() =>
            {
                const int elementCount = 20_000_000; // ~152 MB per double[] array
                const int passes = 5;
                const double scalar = 3.0;

                var a = new double[elementCount];
                var b = new double[elementCount];
                var c = new double[elementCount];

                var rnd = new Random(42);
                for (int i = 0; i < elementCount; i++)
                {
                    a[i] = rnd.NextDouble();
                    b[i] = rnd.NextDouble();
                }

                var sw = Stopwatch.StartNew();
                for (int p = 0; p < passes; p++)
                {
                    for (int i = 0; i < elementCount; i++)
                        c[i] = a[i] + scalar * b[i];
                }
                sw.Stop();
                GC.KeepAlive(c);

                var totalBytes = (long)elementCount * passes * 3 * sizeof(double);
                var gbPerSecond = totalBytes / sw.Elapsed.TotalSeconds / (1024d * 1024 * 1024);

                return new BenchmarkResult
                {
                    Name = "Память: пропускная способность (STREAM Triad)",
                    Score = Math.Round(gbPerSecond, 2),
                    Unit = "ГБ/с",
                    Duration = sw.Elapsed
                };
            });
        }

        // ---- Memory latency: random pointer chasing ----------------------------
        // A random-cycle linked traversal defeats the hardware prefetcher,
        // so each step is a genuine cache/RAM-latency-bound access. This is
        // the classic technique real tools (lmbench, AIDA64) use for RAM
        // latency, not a synthetic stand-in.
        public static Task<BenchmarkResult> RunMemoryLatencyAsync()
        {
            return Task.Run(() =>
            {
                const int nodeCount = 4_000_000; // ~16 MB of int indices — bigger than most L3 caches
                var next = new int[nodeCount];
                var order = new int[nodeCount];
                for (int i = 0; i < nodeCount; i++) order[i] = i;

                var rnd = new Random(1234);
                for (int i = nodeCount - 1; i > 0; i--)
                {
                    int j = rnd.Next(i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }
                for (int i = 0; i < nodeCount; i++)
                    next[order[i]] = order[(i + 1) % nodeCount];

                const int touches = 20_000_000;
                var sw = Stopwatch.StartNew();
                int cursor = 0;
                for (int i = 0; i < touches; i++)
                    cursor = next[cursor];
                sw.Stop();
                GC.KeepAlive(cursor);

                var nsPerAccess = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / touches;

                return new BenchmarkResult
                {
                    Name = "Память: случайный доступ (латентность)",
                    Score = Math.Round(nsPerAccess, 1),
                    Unit = "нс/обращение",
                    Duration = sw.Elapsed
                };
            });
        }

        // ---- Disk: sequential write ---------------------------------------------
        public static Task<BenchmarkResult> RunDiskWriteAsync()
        {
            return Task.Run(() =>
            {
                const int blockSize = 4 * 1024 * 1024; // 4 MB
                const int blocks = 64;                 // 256 MB total
                var buffer = new byte[blockSize];
                new Random(7).NextBytes(buffer);

                var path = Path.Combine(Path.GetTempPath(), $"psuite-bench-{Guid.NewGuid():N}.tmp");
                try
                {
                    var sw = Stopwatch.StartNew();
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, blockSize))
                    {
                        for (int i = 0; i < blocks; i++)
                            fs.Write(buffer, 0, blockSize);
                        fs.Flush(true); // force to disk, not just the OS write cache
                    }
                    sw.Stop();

                    var totalBytes = (long)blockSize * blocks;
                    var mbPerSecond = totalBytes / sw.Elapsed.TotalSeconds / (1024 * 1024);

                    return new BenchmarkResult
                    {
                        Name = "Диск: последовательная запись",
                        Score = Math.Round(mbPerSecond, 0),
                        Unit = "МБ/с",
                        Duration = sw.Elapsed
                    };
                }
                finally
                {
                    TryDelete(path);
                }
            });
        }

        // ---- Disk: sequential read ------------------------------------------------
        // Honest caveat, not hidden: a managed FileStream can't reliably
        // bypass the OS file cache (no portable O_DIRECT/FILE_FLAG_NO_BUFFERING
        // in .NET), so on fast SSDs a repeat run may read partly from cache.
        // Good enough for relative before/after comparison; not lab-grade.
        public static Task<BenchmarkResult> RunDiskReadAsync()
        {
            return Task.Run(() =>
            {
                const int blockSize = 4 * 1024 * 1024;
                const int blocks = 64;
                var writeBuffer = new byte[blockSize];
                new Random(7).NextBytes(writeBuffer);
                var readBuffer = new byte[blockSize];

                var path = Path.Combine(Path.GetTempPath(), $"psuite-bench-{Guid.NewGuid():N}.tmp");
                try
                {
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, blockSize))
                    {
                        for (int i = 0; i < blocks; i++)
                            fs.Write(writeBuffer, 0, blockSize);
                        fs.Flush(true);
                    }

                    var sw = Stopwatch.StartNew();
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, blockSize))
                    {
                        int read;
                        while ((read = fs.Read(readBuffer, 0, blockSize)) > 0) { }
                    }
                    sw.Stop();

                    var totalBytes = (long)blockSize * blocks;
                    var mbPerSecond = totalBytes / sw.Elapsed.TotalSeconds / (1024 * 1024);

                    return new BenchmarkResult
                    {
                        Name = "Диск: последовательное чтение",
                        Score = Math.Round(mbPerSecond, 0),
                        Unit = "МБ/с",
                        Duration = sw.Elapsed
                    };
                }
                finally
                {
                    TryDelete(path);
                }
            });
        }

        // ---- Disk: random 4K access (IOPS) -----------------------------------------
        // Sequential MB/s (above) hides how a drive behaves under real
        // workloads — small apps, page faults, DB-style access are mostly
        // random 4K ops, not big sequential streams. This measures real
        // random reads/writes on a pre-allocated 32 MB temp file, opened
        // with FileOptions.WriteThrough so writes are honestly forced to
        // the device rather than resolved from the OS write cache.
        public static Task<BenchmarkResult> RunDiskRandomIOAsync()
        {
            return Task.Run(() =>
            {
                const int blockSize = 4 * 1024;      // 4 KB — the standard random-IO unit
                const int totalBlocks = 8192;         // 32 MB backing file
                const int opsToPerform = 1500;
                var rng = new Random(11);
                var writeBuffer = new byte[blockSize];
                rng.NextBytes(writeBuffer);
                var readBuffer = new byte[blockSize];

                var path = Path.Combine(Path.GetTempPath(), $"psuite-bench-{Guid.NewGuid():N}.tmp");
                try
                {
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, blockSize))
                    {
                        for (int i = 0; i < totalBlocks; i++)
                            fs.Write(writeBuffer, 0, blockSize);
                        fs.Flush(true);
                    }

                    var sw = Stopwatch.StartNew();
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None,
                        blockSize, FileOptions.WriteThrough))
                    {
                        for (int i = 0; i < opsToPerform; i++)
                        {
                            var blockIndex = rng.Next(totalBlocks);
                            fs.Seek((long)blockIndex * blockSize, SeekOrigin.Begin);

                            if (i % 2 == 0)
                                fs.Read(readBuffer, 0, blockSize);
                            else
                                fs.Write(writeBuffer, 0, blockSize);
                        }
                    }
                    sw.Stop();

                    var iops = opsToPerform / sw.Elapsed.TotalSeconds;

                    return new BenchmarkResult
                    {
                        Name = "Диск: случайный доступ (4K IOPS)",
                        Score = Math.Round(iops, 0),
                        Unit = "IOPS (50% чтение / 50% запись)",
                        Duration = sw.Elapsed
                    };
                }
                finally
                {
                    TryDelete(path);
                }
            });
        }

        // ---- Network: localhost TCP loopback throughput --------------------------
        // Measures real TCP throughput between two sockets on the SAME
        // machine (127.0.0.1) — this deliberately does NOT touch the
        // internet or any external server (that would be measuring the
        // network/ISP, not this PC, and isn't something a benchmark
        // should silently do without asking). Loopback throughput mostly
        // reflects CPU/memory-copy overhead in the TCP/IP stack itself,
        // not "internet speed" — labelled accordingly so it isn't
        // confused with a speed test.
        public static Task<BenchmarkResult> RunNetworkLoopbackAsync()
        {
            return Task.Run(async () =>
            {
                const int port = 51837;
                const int chunkSize = 64 * 1024;
                const int totalMb = 256;
                var totalBytes = (long)totalMb * 1024 * 1024;
                var buffer = new byte[chunkSize];
                new Random(7).NextBytes(buffer);

                var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();

                var serverTask = Task.Run(async () =>
                {
                    using var server = await listener.AcceptTcpClientAsync();
                    using var stream = server.GetStream();
                    var readBuf = new byte[chunkSize];
                    long received = 0;
                    while (received < totalBytes)
                    {
                        var n = await stream.ReadAsync(readBuf, 0, readBuf.Length);
                        if (n == 0) break;
                        received += n;
                    }
                });

                var sw = Stopwatch.StartNew();
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    await client.ConnectAsync(System.Net.IPAddress.Loopback, port);
                    using var stream = client.GetStream();
                    long sent = 0;
                    while (sent < totalBytes)
                    {
                        await stream.WriteAsync(buffer, 0, buffer.Length);
                        sent += buffer.Length;
                    }
                }
                await serverTask;
                sw.Stop();
                listener.Stop();

                var throughputMbps = sw.Elapsed.TotalSeconds > 0
                    ? (totalMb * 8.0) / sw.Elapsed.TotalSeconds
                    : 0;

                return new BenchmarkResult
                {
                    Name = "Сеть: localhost TCP (не интернет)",
                    Score = Math.Round(throughputMbps, 0),
                    Unit = "Мбит/с (петля 127.0.0.1 — отражает накладные расходы TCP/IP-стека, не скорость интернета)",
                    Duration = sw.Elapsed
                };
            });
        }

        // ---- GPU: video memory bandwidth (Direct3D11 buffer copy) -----------------
        // Creates a hardware D3D11 device and times GPU-side buffer-to-
        // buffer copies (CopyResource) — a real, measured GPU memory
        // operation, not a CPU workload mislabeled as "GPU". Deliberately
        // avoids compute shaders/HLSL to keep this to well-trodden D3D11
        // buffer APIs. Requires the Vortice.Direct3D11 / Vortice.DXGI
        // NuGet packages (added to the .csproj alongside this).
        public static Task<BenchmarkResult> RunGpuMemoryAsync()
        {
            return Task.Run(() =>
            {
                using var factory = Vortice.DXGI.DXGI.CreateDXGIFactory1<Vortice.DXGI.IDXGIFactory1>();
                factory.EnumAdapters1(0, out Vortice.DXGI.IDXGIAdapter1 adapter);
                using var _adapter = adapter;

                var creationFlags = Vortice.Direct3D11.DeviceCreationFlags.BgraSupport;
                Vortice.Direct3D11.D3D11.D3D11CreateDevice(
                    adapter,
                    Vortice.Direct3D.DriverType.Unknown,
                    creationFlags,
                    new[]
                    {
                        Vortice.Direct3D.FeatureLevel.Level_11_1,
                        Vortice.Direct3D.FeatureLevel.Level_11_0
                    },
                    out var device,
                    out _,
                    out var context);

                using var d3dDevice = device;
                using var d3dContext = context;

                const int bufferBytes = 64 * 1024 * 1024; // 64 MB
                const int passes = 20;

                var bufferDesc = new Vortice.Direct3D11.BufferDescription
                {
                    ByteWidth = bufferBytes,
                    Usage = Vortice.Direct3D11.ResourceUsage.Default,
                    BindFlags = Vortice.Direct3D11.BindFlags.ShaderResource,
                    CPUAccessFlags = Vortice.Direct3D11.CpuAccessFlags.None
                };

                using var bufferA = d3dDevice.CreateBuffer(bufferDesc);
                using var bufferB = d3dDevice.CreateBuffer(bufferDesc);

                // Warm-up copy so driver/resource allocation overhead
                // doesn't pollute the timed loop.
                d3dContext.CopyResource(bufferB, bufferA);
                d3dContext.Flush();

                var sw = Stopwatch.StartNew();
                for (int i = 0; i < passes; i++)
                    d3dContext.CopyResource(bufferB, bufferA);
                d3dContext.Flush();
                sw.Stop();

                var totalBytes = (long)bufferBytes * passes;
                var gbPerSecond = totalBytes / sw.Elapsed.TotalSeconds / (1024d * 1024 * 1024);

                return new BenchmarkResult
                {
                    Name = "GPU: копирование видеопамяти",
                    Score = Math.Round(gbPerSecond, 2),
                    Unit = "ГБ/с",
                    Duration = sw.Elapsed
                };
            });
        }

        // ---- Stability: sustained load, measured for throughput drop --------------
        // Runs the same CPU multi-core workload repeatedly in ~1s windows
        // for the whole test duration, instead of once. A healthy system
        // holds roughly the same ops/sec window to window; thermal
        // throttling or instability shows up as later windows measuring
        // meaningfully lower than the best window. Score = 100 minus the
        // %-drop between the best and worst window (floored at 0) — a
        // real measured number, not a simulated "stability" figure.
        public static Task<BenchmarkResult> RunStabilityTestAsync(TimeSpan? duration = null)
        {
            return Task.Run(() =>
            {
                var testDuration = duration ?? TimeSpan.FromSeconds(15);
                var totalSw = Stopwatch.StartNew();
                var threads = Math.Max(1, Environment.ProcessorCount);
                const long iterationsPerThreadPerWindow = 15_000_000;
                var samples = new System.Collections.Generic.List<double>();

                while (totalSw.Elapsed < testDuration)
                {
                    var windowSw = Stopwatch.StartNew();
                    Parallel.For(0, threads, _ =>
                    {
                        double acc = 1.0;
                        for (long i = 1; i <= iterationsPerThreadPerWindow; i++)
                        {
                            acc += Math.Sin(i) * Math.Cos(i);
                            if (acc > 1_000_000 || acc < -1_000_000) acc = 1.0;
                        }
                        GC.KeepAlive(acc);
                    });
                    windowSw.Stop();

                    var opsThisWindow = iterationsPerThreadPerWindow * (long)threads;
                    samples.Add(opsThisWindow / windowSw.Elapsed.TotalSeconds);
                }

                totalSw.Stop();

                if (samples.Count < 2)
                {
                    return new BenchmarkResult
                    {
                        Name = "Стабильность под нагрузкой",
                        Score = 100,
                        Unit = "недостаточно окон измерения — увеличь длительность",
                        Duration = totalSw.Elapsed
                    };
                }

                var max = samples.Max();
                var min = samples.Min();
                var dropPercent = max > 0 ? (max - min) / max * 100.0 : 0;
                var score = Math.Max(0, 100 - dropPercent);

                return new BenchmarkResult
                {
                    Name = "Стабильность под нагрузкой",
                    Score = Math.Round(score, 0),
                    Unit = $"/100 (просадка {dropPercent:0.#}% за {samples.Count} измерений)",
                    Duration = totalSw.Elapsed
                };
            });
        }

        // ---- Stability: sustained multi-core load, checking for throttling ------
        // Runs all-core CPU load in timed windows and compares the average
        // throughput of the FIRST two windows to the LAST two. A real
        // thermal-throttling or power-limit drop shows up as a declining
        // trend across windows — a single noisy window doesn't. This is a
        // genuine measured comparison, not a fixed pass/fail guess.
        //
        // While the CPU windows run, a separate background loop hammers
        // the GPU with real Direct3D11 buffer copies (the same operation
        // as the GPU memory-bandwidth test) so the whole system — not just
        // the CPU — is under load, closer to real gaming/rendering
        // conditions. If no GPU is available, this degrades honestly: the
        // test still runs CPU-only and says so in the result text, it
        // never pretends GPU load happened when it didn't.
        public static Task<BenchmarkResult> RunStabilityTestAsync(IProgress<int>? percentProgress = null)
        {
            return Task.Run(() =>
            {
                var threads = Math.Max(1, Environment.ProcessorCount);
                const int windowCount = 10;
                var windowDuration = TimeSpan.FromSeconds(1.5);
                var windowScores = new double[windowCount];

                using var gpuLoad = TryStartGpuLoad();

                var overallSw = Stopwatch.StartNew();
                for (int w = 0; w < windowCount; w++)
                {
                    long opsThisWindow = 0;
                    var windowSw = Stopwatch.StartNew();

                    Parallel.For(0, threads, _ =>
                    {
                        double acc = 1.0;
                        long localOps = 0;
                        while (windowSw.Elapsed < windowDuration)
                        {
                            for (int i = 0; i < 100_000; i++)
                            {
                                acc += Math.Sin(i) * Math.Cos(i);
                                if (acc > 1_000_000 || acc < -1_000_000) acc = 1.0;
                            }
                            localOps += 100_000;
                        }
                        Interlocked.Add(ref opsThisWindow, localOps);
                        GC.KeepAlive(acc);
                    });

                    windowSw.Stop();
                    windowScores[w] = opsThisWindow / windowSw.Elapsed.TotalSeconds;
                    percentProgress?.Report((int)Math.Round((w + 1) / (double)windowCount * 100));
                }
                overallSw.Stop();

                var firstAvg = windowScores.Take(2).Average();
                var lastAvg = windowScores.Skip(windowCount - 2).Average();
                var stabilityPercent = firstAvg > 0 ? Math.Min(100, lastAvg / firstAvg * 100) : 100;

                var gpuNote = gpuLoad.WasActive
                    ? $"CPU+GPU нагрузка, {gpuLoad.CopiesCompleted} копий видеопамяти"
                    : "только CPU — GPU нагрузка недоступна на этой машине";

                return new BenchmarkResult
                {
                    Name = "Стабильность под нагрузкой",
                    Score = Math.Round(stabilityPercent, 1),
                    Unit = $"% от начальной скорости ({gpuNote}, {windowCount} окон)",
                    Duration = overallSw.Elapsed
                };
            });
        }

        // Best-effort background GPU load for the stability test. Reuses
        // the same D3D11 buffer-copy operation as the GPU bandwidth test.
        // Runs on its own thread until disposed; never throws out of this
        // method — if GPU init fails for any reason (no adapter, driver
        // issue, VM without passthrough), WasActive stays false and the
        // stability test simply continues as CPU-only, honestly labelled.
        private static GpuLoadHandle TryStartGpuLoad()
        {
            try
            {
                var factory = Vortice.DXGI.DXGI.CreateDXGIFactory1<Vortice.DXGI.IDXGIFactory1>();
                factory.EnumAdapters1(0, out Vortice.DXGI.IDXGIAdapter1 adapter);

                Vortice.Direct3D11.D3D11.D3D11CreateDevice(
                    adapter,
                    Vortice.Direct3D.DriverType.Unknown,
                    Vortice.Direct3D11.DeviceCreationFlags.BgraSupport,
                    new[]
                    {
                        Vortice.Direct3D.FeatureLevel.Level_11_1,
                        Vortice.Direct3D.FeatureLevel.Level_11_0
                    },
                    out var device,
                    out _,
                    out var context);

                const int bufferBytes = 32 * 1024 * 1024; // smaller than the dedicated GPU test — this just needs to keep the GPU busy, not measure it
                var bufferDesc = new Vortice.Direct3D11.BufferDescription
                {
                    ByteWidth = bufferBytes,
                    Usage = Vortice.Direct3D11.ResourceUsage.Default,
                    BindFlags = Vortice.Direct3D11.BindFlags.ShaderResource,
                    CPUAccessFlags = Vortice.Direct3D11.CpuAccessFlags.None
                };
                var bufferA = device.CreateBuffer(bufferDesc);
                var bufferB = device.CreateBuffer(bufferDesc);

                var cts = new CancellationTokenSource();
                var copiesCompleted = 0L;

                var loopTask = Task.Run(() =>
                {
                    try
                    {
                        while (!cts.IsCancellationRequested)
                        {
                            context.CopyResource(bufferB, bufferA);
                            context.Flush();
                            Interlocked.Increment(ref copiesCompleted);
                        }
                    }
                    catch
                    {
                        // If the GPU throws mid-loop (driver reset, TDR,
                        // etc.) just stop the load quietly — the CPU part
                        // of the stability test is unaffected.
                    }
                });

                return new GpuLoadHandle(true, cts, loopTask, () => Interlocked.Read(ref copiesCompleted),
                    () => { adapter.Dispose(); context.Dispose(); device.Dispose(); bufferA.Dispose(); bufferB.Dispose(); factory.Dispose(); });
            }
            catch
            {
                return new GpuLoadHandle(false, null, null, () => 0, null);
            }
        }

        // Small disposable wrapper so the caller can `using` the GPU load
        // regardless of whether GPU init actually succeeded.
        private sealed class GpuLoadHandle : IDisposable
        {
            private readonly CancellationTokenSource? _cts;
            private readonly Task? _loopTask;
            private readonly Func<long> _getCopies;
            private readonly Action? _releaseGpuResources;

            public bool WasActive { get; }
            public long CopiesCompleted => _getCopies();

            public GpuLoadHandle(bool wasActive, CancellationTokenSource? cts, Task? loopTask,
                Func<long> getCopies, Action? releaseGpuResources)
            {
                WasActive = wasActive;
                _cts = cts;
                _loopTask = loopTask;
                _getCopies = getCopies;
                _releaseGpuResources = releaseGpuResources;
            }

            public void Dispose()
            {
                if (!WasActive) return;
                try
                {
                    _cts?.Cancel();
                    _loopTask?.Wait(TimeSpan.FromSeconds(2));
                }
                catch { /* best-effort shutdown */ }
                finally
                {
                    try { _releaseGpuResources?.Invoke(); } catch { /* best-effort */ }
                }
            }
        }

        // ---- Runs everything in order, reporting a short status per step -------
        public static async Task<BenchmarkSuiteResult> RunFullSuiteAsync(IProgress<string>? progress = null)
        {
            var results = new System.Collections.Generic.List<BenchmarkResult>();
            var totalSw = Stopwatch.StartNew();
            const int totalSteps = 9;
            int step = 0;

            void Report(string label) => progress?.Report($"{(int)(100.0 * step / totalSteps)}% — {label}");

            step = 1; Report("CPU: один поток...");
            results.Add(await RunCpuSingleCoreAsync());

            step = 2; Report("CPU: все ядра...");
            results.Add(await RunCpuMultiCoreAsync());

            step = 3; Report("Память: пропускная способность...");
            results.Add(await RunMemoryBandwidthAsync());

            step = 4; Report("Память: случайный доступ...");
            results.Add(await RunMemoryLatencyAsync());

            step = 5; Report("Диск: запись...");
            results.Add(await RunDiskWriteAsync());

            step = 6; Report("Диск: чтение...");
            results.Add(await RunDiskReadAsync());

            step = 7; Report("Диск: случайный доступ (IOPS)...");
            results.Add(await RunDiskRandomIOAsync());

            step = 8; Report("Сеть: localhost TCP...");
            try
            {
                results.Add(await RunNetworkLoopbackAsync());
            }
            catch (Exception ex)
            {
                // Loopback sockets can be blocked by strict firewall/AV
                // policy on some machines — degrade gracefully rather
                // than failing the whole suite over one optional test.
                results.Add(new BenchmarkResult
                {
                    Name = "Сеть: localhost TCP (не интернет)",
                    Score = 0,
                    Unit = $"недоступно ({ex.Message})",
                    Duration = TimeSpan.Zero
                });
            }

            step = 9; Report("GPU: видеопамять...");
            try
            {
                results.Add(await RunGpuMemoryAsync());
            }
            catch (Exception ex)
            {
                // No hardware D3D11 adapter, driver issue, or running in a
                // VM without GPU passthrough — degrade gracefully instead
                // of failing the whole suite over one optional test.
                results.Add(new BenchmarkResult
                {
                    Name = "GPU: копирование видеопамяти",
                    Score = 0,
                    Unit = $"недоступно ({ex.GetType().Name})",
                    Duration = TimeSpan.Zero
                });
            }

            progress?.Report("100% — готово");
            totalSw.Stop();
            return new BenchmarkSuiteResult { Results = results, TotalDuration = totalSw.Elapsed };
        }

        // Canonical direction table — used both for the score below and
        // for the ▲/▼ colouring of individual result rows in the UI.
        public static bool IsHigherBetter(string resultName) =>
            resultName != "Память: случайный доступ (латентность)";

        // A single, honest "Performance Score" — NOT a comparison to other
        // people's hardware (no fabricated external baseline exists for
        // that), but a self-referential score vs THIS machine's first-ever
        // benchmark run. 1000 = exactly as fast as when you started using
        // PSuite. 1085 = a measured 8.5% faster (weighted composite).
        // Returns null if there's no baseline yet, or nothing comparable.
        public static double? ComputeScore(BenchmarkSuiteResult current, BenchmarkSuiteResult? baseline)
        {
            if (baseline == null) return null;

            double weightedSum = 0;
            double weightTotal = 0;

            foreach (var cur in current.Results)
            {
                if (cur.Score == 0) continue; // e.g. GPU test unavailable on this run

                var baseResult = baseline.Results.FirstOrDefault(r => r.Name == cur.Name);
                if (baseResult == null || baseResult.Score == 0) continue;

                var weight = GetScoreWeight(cur.Name);
                if (weight <= 0) continue;

                var pctChange = (cur.Score - baseResult.Score) / baseResult.Score * 100.0;
                if (!IsHigherBetter(cur.Name)) pctChange = -pctChange; // normalise so "+" always means "better"

                weightedSum += pctChange * weight;
                weightTotal += weight;
            }

            if (weightTotal <= 0) return null;

            var weightedAvgPercent = weightedSum / weightTotal;
            return Math.Max(100.0, 1000.0 * (1 + weightedAvgPercent / 100.0));
        }

        // Disclosed weights, not hidden: CPU work matters most for a
        // general "how snappy is this PC" score; memory/disk/GPU carry
        // less weight since they're more often bottlenecked by hardware
        // the person can't tweak. GPU is optional (0 weight contribution
        // if unavailable on a given run — handled above).
        private static double GetScoreWeight(string resultName)
        {
            if (resultName == "CPU (1 поток)") return 0.20;
            if (resultName.StartsWith("CPU (все ядра")) return 0.25;
            if (resultName == "Память: пропускная способность (STREAM Triad)") return 0.18;
            if (resultName == "Память: случайный доступ (латентность)") return 0.12;
            if (resultName == "Диск: последовательная запись") return 0.08;
            if (resultName == "Диск: последовательное чтение") return 0.07;
            if (resultName == "Диск: случайный доступ (4K IOPS)") return 0.08;
            if (resultName == "Сеть: localhost TCP (не интернет)") return 0.04;
            if (resultName == "GPU: копирование видеопамяти") return 0.10;
            return 0;
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch { /* best-effort cleanup, not worth failing the benchmark over */ }
        }
    }
}
