namespace MakoMaster;
internal static class WordMatching
{
    internal static string Normalize(string text) => string.Concat(text.Where(c => !char.IsPunctuation(c) && !char.IsWhiteSpace(c)));
    internal static bool Same(string a, string b) => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    // The IME renders a typed v as ü and may insert syllable separators.
    // Keep this separate from target/candidate matching, where spelling is literal.
    internal static string InputBuffer(string text) => Normalize(text.Normalize(System.Text.NormalizationForm.FormC))
        .Replace('ü', 'v').Replace('Ü', 'V');
    internal static bool IsBarrierColor(uint rgba) => (rgba & 0x00FFFFFF) == 0x0074F8A0;
    internal static bool KnownInterruptibleSkill(string skill) => Same(skill, "？针刺") || Same(skill, "霹雳炸弹");
    internal static string Slot(string path) { var next = path.IndexOf('/', 1); return next < 0 ? path : path[..next]; }
}
internal sealed record WordObservation(string Path, string Text, bool TextVisible, bool ContainerVisible, double DistanceSquared, uint TextColor = 0xFFFFFFFF);
internal sealed class WordResolver
{
    private readonly Dictionary<string, string> remembered = new();
    private readonly Dictionary<string, string> barriers = new();
    internal void Reset() { remembered.Clear(); barriers.Clear(); }
    internal List<TrialWord> Resolve(IEnumerable<WordObservation> observations, IReadOnlyDictionary<string, DictionaryEntry> dictionary, IReadOnlyDictionary<string, string> skills, IReadOnlySet<string>? interruptible = null,
        IReadOnlyDictionary<string, bool>? interruptibleSlots = null)
    {
        var result = new List<TrialWord>();
        foreach (var item in observations)
        {
            var slot = WordMatching.Slot(item.Path);
            if (!item.ContainerVisible && !skills.ContainsKey(slot)) { remembered.Remove(slot); barriers.Remove(slot); continue; }
            var text = item.Text; var obscured = !item.TextVisible;
            if (!dictionary.Values.Any(e => WordMatching.Same(e.Word, text))
                && text.IndexOfAny(['?', '？', '*', '＊', '■', '□', '●', '○']) >= 0)
            {
                if (remembered.TryGetValue(slot, out var previous)) text = previous;
                else
                {
                    var pattern = string.Concat(text.Where(c => !char.IsPunctuation(c) || c is '?' or '？' or '*' or '＊'));
                    var matches = dictionary.Values.Where(e => MatchesMask(pattern, WordMatching.Normalize(e.Word))).Select(e => e.Word).Distinct().Take(2).ToArray();
                    if (matches.Length != 1) continue;
                    text = matches[0];
                }
                obscured = true;
            }
            if (string.IsNullOrWhiteSpace(text)) { if (!remembered.TryGetValue(slot, out text)) continue; obscured = true; }
            remembered[slot] = text;
            skills.TryGetValue(slot, out var skill);
            var normalized = WordMatching.Normalize(text);
            if (WordMatching.IsBarrierColor(item.TextColor)) barriers[slot] = normalized;
            else if (barriers.TryGetValue(slot, out var barrierWord) && barrierWord != normalized) barriers.Remove(slot);
            var canInterrupt = interruptibleSlots != null && interruptibleSlots.TryGetValue(slot, out var liveFlag)
                ? liveFlag : skill != null && interruptible?.Contains(skill) == true;
            if (skill != null && WordMatching.KnownInterruptibleSkill(skill)) canInterrupt = true;
            var priority = barriers.ContainsKey(slot) ? 3 : skill == null ? 0 : canInterrupt ? 2 : 1;
            result.Add(new(item.Path, text, item.DistanceSquared, priority, skill ?? "", obscured));
        }
        return result;
    }
    private static bool MatchesMask(string pattern, string word)
    {
        if (pattern.Length != word.Length) return false;
        for (var i = 0; i < pattern.Length; i++)
            if (pattern[i] is not ('?' or '？' or '*' or '＊' or '■' or '□' or '●' or '○') && pattern[i] != word[i]) return false;
        return true;
    }
}
