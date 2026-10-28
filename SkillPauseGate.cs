namespace MakoMaster;
internal sealed record CastObservation(string Slot, string Skill, double? RemainingSeconds, double? Progress = null);
internal sealed class CastProgressTracker
{
    private sealed record Sample(long At, double Progress);
    private sealed class Track(string skill) { internal string Skill = skill; internal readonly List<Sample> Samples = []; }
    private readonly Dictionary<string, Track> tracks = new();
    internal double? Observe(string slot, string skill, double? progress, long now)
    {
        if (progress is not (>= 0 and <= 1)) return null;
        if (!tracks.TryGetValue(slot, out var track) || track.Skill != skill || track.Samples.Count > 0 &&
            (progress < track.Samples[^1].Progress - 0.05 || now - track.Samples[^1].At > 1000)) tracks[slot] = track = new(skill);
        track.Samples.Add(new(now, progress.Value));
        track.Samples.RemoveAll(s => now - s.At > 1500);
        var first = track.Samples[0]; var durationMs = now - first.At; var difference = progress.Value - first.Progress;
        if (durationMs < 200 || difference < 0.02) return null;
        var remaining = (1 - progress.Value) * durationMs / difference / 1000;
        return remaining is >= 0 and <= 120 ? remaining : null;
    }
    internal void Keep(IReadOnlySet<string> slots) { foreach (var slot in tracks.Keys.Where(s => !slots.Contains(s)).ToArray()) tracks.Remove(slot); }
    internal void Reset() => tracks.Clear();
}
internal sealed class SkillPauseGate
{
    internal static bool IsDangerous(string skill) => WordMatching.Same(skill, "运动体探知干扰器") || WordMatching.Same(skill, "诅咒视线");
    internal const int BeforeMs = 500, AfterMs = 1500;
    private readonly Dictionary<string, long> ends = new();
    private readonly HashSet<string> previouslyVisible = new();
    private readonly CastProgressTracker progressTracker = new();
    internal bool AwaitingProgress { get; private set; }
    internal bool Tick(IEnumerable<CastObservation> casts, long now)
    {
        var active = casts.Where(c => IsDangerous(c.Skill)).ToArray(); var visible = active.Select(c => c.Slot).ToHashSet();
        AwaitingProgress = false;
        foreach (var c in active)
        {
            var remaining = c.RemainingSeconds is >= 0 and <= 120 ? c.RemainingSeconds : progressTracker.Observe(c.Slot, c.Skill, c.Progress, now);
            if (remaining is >= 0 and <= 120) ends[c.Slot] = now + (long)Math.Round(remaining.Value * 1000);
            else AwaitingProgress = true; // Unknown timing never freezes the entire cast.
        }
        foreach (var slot in previouslyVisible.Where(s => !visible.Contains(s))) ends[slot] = now;
        previouslyVisible.Clear(); previouslyVisible.UnionWith(visible); progressTracker.Keep(visible);
        foreach (var slot in ends.Where(p => now > p.Value + AfterMs && !visible.Contains(p.Key)).Select(p => p.Key).ToArray()) ends.Remove(slot);
        return ends.Any(p => now >= p.Value - BeforeMs && now <= p.Value + AfterMs);
    }
    internal void Reset() { ends.Clear(); previouslyVisible.Clear(); progressTracker.Reset(); AwaitingProgress = false; }
}
