using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Xbim.Geometry.Scene;

/// <summary>
/// Collects timing data for geometry context creation phases and operations.
/// All methods are thread-safe. When <see cref="Enabled"/> is false (the default),
/// timing calls are no-ops with negligible overhead.
/// </summary>
internal sealed class ContextDiagnostics
{
    public bool Enabled { get; set; }

    // ── Accumulators (thread-safe via Interlocked) ──
    private long _createTicks, _meshTicks, _directTessTicks;
    private long _boolUnionTicks, _boolCutTicks, _triangTicks;
    private int _createCount, _meshCount, _directTessCount;
    private int _boolUnionCount, _boolCutCount, _triangCount;
    private int _createFailCount;
    private readonly ConcurrentDictionary<string, (long ticks, int count)> _createByType = new();
    private readonly ConcurrentDictionary<string, (long ticks, int count)> _meshByType = new();

    /// <summary>
    /// Starts a phase timer that writes its elapsed time to the console on dispose.
    /// Used for top-level sequential phases in CreateContext.
    /// </summary>
    public PhaseScope TrackPhase(string name)
        => Enabled ? new PhaseScope(name) : default;

    /// <summary>
    /// Starts an operation timer that accumulates ticks into the given category on dispose.
    /// Used inside parallel loops for boolean ops, triangulation, and direct tessellation.
    /// </summary>
    public OpScope Track(DiagOp op)
        => Enabled ? new OpScope(this, op) : default;

    /// <summary>
    /// Starts a manually-controlled timer for operations that need post-hoc categorization
    /// (e.g. recording the IFC type name after the operation completes).
    /// Call <see cref="RecordCreate"/> or <see cref="RecordMesh"/> with the returned scope.
    /// </summary>
    public TimerScope StartTimer()
        => Enabled ? new TimerScope(start: true) : default;

    /// <summary>
    /// Records an Engine.Create call with its IFC type and success/failure status.
    /// </summary>
    public void RecordCreate(TimerScope timer, string typeName, bool failed)
    {
        if (!Enabled) return;
        long ticks = timer.Stop();
        Interlocked.Add(ref _createTicks, ticks);
        Interlocked.Increment(ref _createCount);
        _createByType.AddOrUpdate(typeName,
            (ticks, 1),
            (_, old) => (old.ticks + ticks, old.count + 1));
        if (failed)
            Interlocked.Increment(ref _createFailCount);
    }

    /// <summary>
    /// Records an Engine.CreateShapeGeometry call with its IFC type.
    /// </summary>
    public void RecordMesh(TimerScope timer, string typeName)
    {
        if (!Enabled) return;
        long ticks = timer.Stop();
        Interlocked.Add(ref _meshTicks, ticks);
        Interlocked.Increment(ref _meshCount);
        _meshByType.AddOrUpdate(typeName,
            (ticks, 1),
            (_, old) => (old.ticks + ticks, old.count + 1));
    }

    /// <summary>
    /// Writes a summary of accumulated timing data.
    /// </summary>
    public void PrintSummary(TextWriter writer = null)
    {
        var w = writer ?? Console.Out;
        double ticksToMs = 1000.0 / Stopwatch.Frequency;

        w.WriteLine("\n── Sub-operation Timing ──");
        WriteOp(w, "Engine.Create",              _createTicks,      _createCount,      ticksToMs, $"fail={_createFailCount}");
        WriteOp(w, "Engine.CreateShapeGeometry",  _meshTicks,        _meshCount,        ticksToMs);
        WriteOp(w, "XbimTessellator.Mesh",        _directTessTicks,  _directTessCount,  ticksToMs);
        WriteOp(w, "Boolean Union",               _boolUnionTicks,   _boolUnionCount,   ticksToMs);
        WriteOp(w, "Boolean Cut",                 _boolCutTicks,     _boolCutCount,     ticksToMs);
        WriteOp(w, "WriteTriangulation",          _triangTicks,      _triangCount,      ticksToMs);

        WriteByType(w, "Engine.Create by IFC type (top 15)", _createByType, ticksToMs);
        WriteByType(w, "Engine.CreateShapeGeometry by IFC type (top 15)", _meshByType, ticksToMs);
    }

    // ── Internal accumulation ──

    internal void Accumulate(DiagOp op, long elapsedTicks)
    {
        switch (op)
        {
            case DiagOp.BooleanUnion:
                Interlocked.Add(ref _boolUnionTicks, elapsedTicks);
                Interlocked.Increment(ref _boolUnionCount);
                break;
            case DiagOp.BooleanCut:
                Interlocked.Add(ref _boolCutTicks, elapsedTicks);
                Interlocked.Increment(ref _boolCutCount);
                break;
            case DiagOp.Triangulation:
                Interlocked.Add(ref _triangTicks, elapsedTicks);
                Interlocked.Increment(ref _triangCount);
                break;
            case DiagOp.DirectTessellation:
                Interlocked.Add(ref _directTessTicks, elapsedTicks);
                Interlocked.Increment(ref _directTessCount);
                break;
        }
    }

    // ── Reporting helpers ──

    private static void WriteOp(TextWriter w, string name, long ticks, int count, double ticksToMs, string extra = null)
    {
        double totalS = ticks * ticksToMs / 1000;
        double avgMs = count > 0 ? ticks * ticksToMs / count : 0;
        string suffix = extra != null ? $", {extra}" : "";
        w.WriteLine($"  {name,-28} {totalS:F2}s  (n={count}{suffix}, avg={avgMs:F1}ms)");
    }

    private static void WriteByType(TextWriter w, string header, ConcurrentDictionary<string, (long ticks, int count)> dict, double ticksToMs)
    {
        if (dict.IsEmpty) return;
        w.WriteLine($"\n── {header} ──");
        foreach (var kv in dict.OrderByDescending(kv => kv.Value.ticks).Take(15))
        {
            var (ticks, count) = kv.Value;
            w.WriteLine($"  {kv.Key,-45} n={count,5}  total={ticks * ticksToMs / 1000:F2}s  avg={ticks * ticksToMs / count:F1}ms");
        }
    }

    // ── Scope types ──

    /// <summary>
    /// Measures a top-level phase and writes timing to the console on dispose.
    /// </summary>
    public struct PhaseScope : IDisposable
    {
        private readonly string _name;
        private readonly Stopwatch _sw;

        internal PhaseScope(string name)
        {
            _name = name;
            _sw = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            if (_sw == null) return;
            _sw.Stop();
            Console.WriteLine($"  {_name,-38} {_sw.Elapsed.TotalSeconds:F2}s");
        }
    }

    /// <summary>
    /// Measures an operation and accumulates ticks into the parent diagnostics on dispose.
    /// </summary>
    public struct OpScope : IDisposable
    {
        private readonly ContextDiagnostics _owner;
        private readonly DiagOp _op;
        private readonly Stopwatch _sw;

        internal OpScope(ContextDiagnostics owner, DiagOp op)
        {
            _owner = owner;
            _op = op;
            _sw = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            if (_sw == null) return;
            _sw.Stop();
            _owner.Accumulate(_op, _sw.ElapsedTicks);
        }
    }

    /// <summary>
    /// A manually-controlled timer for operations that need post-hoc categorization.
    /// </summary>
    public struct TimerScope
    {
        private readonly Stopwatch _sw;

        internal TimerScope(bool start)
        {
            _sw = start ? Stopwatch.StartNew() : null;
        }

        internal long Stop()
        {
            if (_sw == null) return 0;
            _sw.Stop();
            return _sw.ElapsedTicks;
        }
    }
}

/// <summary>
/// Categories for operation-level timing.
/// </summary>
internal enum DiagOp
{
    BooleanUnion,
    BooleanCut,
    Triangulation,
    DirectTessellation
}
