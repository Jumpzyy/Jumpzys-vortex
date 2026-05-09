using System.IO;
using Microsoft.ML;
using Microsoft.ML.Data;
using JumpzysVortex.Services;

namespace JumpzysVortex.ML;

// ── Input ─────────────────────────────────────────────────────
public class BottleneckInput
{
    public float Cpu      { get; set; }
    public float Ram      { get; set; }
    public float Gpu      { get; set; }
    public float Fps      { get; set; }
    public float CpuTemp  { get; set; }
    public float CpuTrend { get; set; }
    public float RamTrend { get; set; }
    public bool  Label    { get; set; }
}

// ── Output ────────────────────────────────────────────────────
public class BottleneckOutput
{
    [ColumnName("PredictedLabel")] public bool  Prediction  { get; set; }
    [ColumnName("Probability")]    public float Probability  { get; set; }
    [ColumnName("Score")]          public float Score        { get; set; }
}

// ── Predictor ─────────────────────────────────────────────────
public class BottleneckPredictor
{
    private static readonly string ModelPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JumpzysVortex", "model.zip");

    private readonly MLContext _ctx = new(seed: 42);
    private PredictionEngine<BottleneckInput, BottleneckOutput>? _engine;

    public bool IsModelLoaded => _engine != null;

    // ── Load ──────────────────────────────────────────────
    public bool TryLoadModel()
    {
        try
        {
            if (!File.Exists(ModelPath)) return false;
            var model = _ctx.Model.Load(ModelPath, out _);
            _engine   = _ctx.Model
                .CreatePredictionEngine<BottleneckInput, BottleneckOutput>(model);
            return true;
        }
        catch { return false; }
    }

    // ── Train ─────────────────────────────────────────────
    public void Train(IList<PerformanceSnapshot> history)
    {
        if (history.Count < 30) return;

        var rows = BuildRows(history);
        if (rows.Count < 20) return;

        var data  = _ctx.Data.LoadFromEnumerable(rows);
        var split = _ctx.Data.TrainTestSplit(data, testFraction: 0.2);

        var pipeline = _ctx.Transforms
            .Concatenate("Features",
                nameof(BottleneckInput.Cpu),
                nameof(BottleneckInput.Ram),
                nameof(BottleneckInput.Gpu),
                nameof(BottleneckInput.Fps),
                nameof(BottleneckInput.CpuTemp),
                nameof(BottleneckInput.CpuTrend),
                nameof(BottleneckInput.RamTrend))
            .Append(_ctx.BinaryClassification.Trainers.FastTree(
                labelColumnName:            nameof(BottleneckInput.Label),
                featureColumnName:          "Features",
                numberOfTrees:              500,
                numberOfLeaves:             20,
                minimumExampleCountPerLeaf: 5));

        var model = pipeline.Fit(split.TrainSet);

        // Evaluate — result intentionally discarded (used for logging only if needed)
        _ = _ctx.BinaryClassification.Evaluate(
                model.Transform(split.TestSet),
                labelColumnName: nameof(BottleneckInput.Label));

        // Save model
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        _ctx.Model.Save(model, data.Schema, ModelPath);
        _engine = _ctx.Model
            .CreatePredictionEngine<BottleneckInput, BottleneckOutput>(model);
    }

    // ── Predict ───────────────────────────────────────────
    public float PredictBottleneckProbability(
        PerformanceSnapshot snap,
        IList<PerformanceSnapshot> history)
    {
        if (_engine == null) return 0f;
        return _engine.Predict(BuildInput(snap, history)).Probability;
    }

    // ── Helpers ───────────────────────────────────────────
    private static List<BottleneckInput> BuildRows(IList<PerformanceSnapshot> history)
    {
        var rows = new List<BottleneckInput>();
        for (int i = 5; i < history.Count; i++)
        {
            var s     = history[i];
            var prev5 = history.Skip(i - 5).Take(5).ToList();
            rows.Add(new BottleneckInput
            {
                Cpu      = s.Cpu,
                Ram      = s.Ram,
                Gpu      = s.Gpu,
                Fps      = s.Fps,
                CpuTemp  = s.CpuTemp,
                CpuTrend = s.Cpu - prev5.First().Cpu,
                RamTrend = s.Ram - prev5.First().Ram,
                Label    = s.Cpu > 85f || s.Ram > 88f,
            });
        }
        return rows;
    }

    private static BottleneckInput BuildInput(
        PerformanceSnapshot snap,
        IList<PerformanceSnapshot> history)
    {
        float cpuTrend = 0f, ramTrend = 0f;
        if (history.Count >= 5)
        {
            var prev5  = history.TakeLast(5).ToList();
            cpuTrend   = snap.Cpu - prev5.First().Cpu;
            ramTrend   = snap.Ram - prev5.First().Ram;
        }
        return new BottleneckInput
        {
            Cpu      = snap.Cpu,
            Ram      = snap.Ram,
            Gpu      = snap.Gpu,
            Fps      = snap.Fps,
            CpuTemp  = snap.CpuTemp,
            CpuTrend = cpuTrend,
            RamTrend = ramTrend,
        };
    }
}
