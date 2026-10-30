namespace MakoMaster;

internal sealed record EnemyUiAnchor(string Slot, float X, float Y, float Width, float Height, float ScaleX, float ScaleY)
{
    internal bool Valid => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Width) && float.IsFinite(Height)
        && float.IsFinite(ScaleX) && float.IsFinite(ScaleY) && Width > 0 && Height > 0 && ScaleX > 0 && ScaleY > 0;
    internal double CenterX => X + Width * ScaleX / 2;
}
internal sealed record CastTargetLink(string CastSlot, string WordSlot, double Error);
internal static class CastTargetMatcher
{
    // Captured layouts place the cast root 16 UI units below the 64-unit word panel.
    // These two addons allocate/reuse components independently; matching node IDs is incorrect.
    internal static List<CastTargetLink> Match(IEnumerable<EnemyUiAnchor> words, IEnumerable<EnemyUiAnchor> casts)
    {
        var wordList = words.Where(w => w.Valid).ToArray();
        var proposals = new List<CastTargetLink>();
        foreach (var cast in casts.Where(c => c.Valid))
        {
            var candidates = wordList.Select(word =>
            {
                var dx = (cast.CenterX - word.CenterX) / cast.ScaleX;
                var dy = (cast.Y - word.Y - (word.Height + 16) * word.ScaleY) / cast.ScaleY;
                return new CastTargetLink(cast.Slot, word.Slot, Math.Sqrt(dx * dx + dy * dy));
            }).OrderBy(p => p.Error).ToArray();
            if (candidates.Length == 0 || candidates[0].Error > 12) continue;
            if (candidates.Length > 1 && candidates[1].Error - candidates[0].Error < 1) continue;
            proposals.Add(candidates[0]);
        }
        // Never silently assign two cast bars to one word when layouts overlap during transitions.
        return proposals.GroupBy(p => p.WordSlot).Where(g => g.Count() == 1).Select(g => g.Single()).ToList();
    }
}
internal static class CastStyleRules
{
    internal static bool? Interruptible(ushort? backgroundPart, bool? markerVisible, bool? countdownVisible)
        => backgroundPart switch { 2 => true, 1 => false, _ => markerVisible == null && countdownVisible == null ? null : markerVisible == true || countdownVisible == true };
}
