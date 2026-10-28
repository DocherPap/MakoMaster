using System.Text;
using System.Text.Json;

namespace MakoMaster;

internal sealed record SeenWord(string Word, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, int Appearances);
internal sealed record FallbackWord(string Word, string Pinyin, string Reason, DateTimeOffset LastSeen, int Count);

// Observes only minigame data. A visible word alone never invents a pronunciation.
internal sealed class VocabularyStore
{
    private readonly string folder;
    private readonly Dictionary<string, DictionaryEntry> effective = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DictionaryEntry> learned = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SeenWord> seen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FallbackWord> fallbacks = new(StringComparer.Ordinal);
    private bool dirtyFallbacks;
    private HashSet<string> previousTargets = [];
    private string roman = "";
    private DateTimeOffset romanAt;
    private Pending? pending;
    private bool dirtySeen, dirtyLearned;
    private static readonly JsonSerializerOptions JsonOptions = new()
    { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    internal IReadOnlyDictionary<string, DictionaryEntry> EffectiveDictionary => effective;
    internal int SeenCount => seen.Count;
    internal int LearnedCount => learned.Count;
    internal bool PersistenceEnabled { get; set; }

    internal VocabularyStore(string folder)
    {
        this.folder = folder;
        foreach (var entry in Read<DictionaryEntry>("dictionary.json")) effective.TryAdd(entry.Word, entry);
        foreach (var entry in Read<DictionaryEntry>("learned-dictionary.json"))
        {
            if (!ValidEntry(entry)) throw new InvalidDataException("学习词库含无效词条，未覆盖原文件。");
            learned.TryAdd(entry.Word, entry); effective.TryAdd(entry.Word, entry);
        }
        foreach (var item in Read<SeenWord>("seen-words.json")) seen.TryAdd(item.Word, item);
        foreach (var item in Read<FallbackWord>("fallback-words.json")) fallbacks.TryAdd(item.Word, item);
        dirtyLearned = !File.Exists(Path.Combine(folder, "learned-dictionary.json"));
    }
    private T[] Read<T>(string name)
    {
        var path = Path.Combine(folder, name);
        return File.Exists(path) ? JsonSerializer.Deserialize<T[]>(File.ReadAllText(path))
            ?? throw new InvalidDataException(name + " 内容无效") : [];
    }
    private static bool ValidEntry(DictionaryEntry entry) => !string.IsNullOrWhiteSpace(entry.Word)
        && entry.Pinyin.Length > 0 && entry.Pinyin.All(c => c is >= 'a' and <= 'z');

    internal void Observe(TrialView view, DateTimeOffset at)
    {
        var targets = view.Words.Select(w => w.Path + "\0" + w.Text).ToHashSet(StringComparer.Ordinal);
        foreach (var word in view.Words.Where(w => w.Text.Length is > 0 and <= 128))
        {
            if (!seen.TryGetValue(word.Text, out var item))
            { seen[word.Text] = new(word.Text, at, at, 1); dirtySeen = true; }
            else
            {
                var newAppearance = !previousTargets.Contains(word.Path + "\0" + word.Text);
                seen[word.Text] = item with { LastSeen = at, Appearances = item.Appearances + (newAppearance ? 1 : 0) };
                dirtySeen = true; // Persist LastSeen as well as new appearances.
            }
        }
        if (pending != null)
        {
            var stillVisible = targets.Contains(pending.Path + "\0" + pending.Word);
            // Require a surviving game view so exiting the game cannot confirm a word.
            if (!stillVisible && view.Words.Count > 0 && view.Buffer.Length == 0 && at - pending.At <= TimeSpan.FromSeconds(5))
            {
                if (!effective.Keys.Any(w => WordMatching.Same(w, pending.Word)))
                {
                    var parts = pending.Roman.Split('\'', StringSplitOptions.RemoveEmptyEntries);
                    var pinyin = string.Concat(parts);
                    var entry = new DictionaryEntry(pending.Word, parts, pinyin, pending.Roman);
                    if (ValidEntry(entry))
                    { learned[entry.Word] = entry; effective[entry.Word] = entry; dirtyLearned = true; }
                }
                pending = null;
            }
            else if (at - pending.At > TimeSpan.FromSeconds(5) || view.Words.Count == 0) pending = null;
        }
        if (view.Words.Count == 0)
        { roman = ""; pending = null; previousTargets = targets; return; }
        if (view.Buffer.Length > 0 && view.Buffer.All(c => c is >= 'a' and <= 'z' || c == '\''))
        { roman = view.Buffer; romanAt = at; }
        else if (view.Buffer.Length > 0)
        {
            var target = view.Words.FirstOrDefault(w => WordMatching.Same(w.Text, view.Buffer));
            if (target != null && roman.Length > 0 && at - romanAt <= TimeSpan.FromSeconds(2))
            { pending = new(target.Path, target.Text, roman, at); roman = ""; }
            else if (target == null) { roman = ""; pending = null; }
        }
        else roman = ""; // Cancellation cannot teach a later, unrelated commit.
        // Candidate lists alone are intentionally insufficient for learning: an
        // abbreviated or unfinished composition can show many unrelated words.
        previousTargets = targets;
    }

    internal void Flush()
    {
        if (!PersistenceEnabled) return;
        Directory.CreateDirectory(folder);
        if (dirtyFallbacks)
        { WriteAtomic("fallback-words.json", fallbacks.Values.OrderBy(x => x.Word).ToArray()); dirtyFallbacks = false; }
        if (dirtySeen)
        { WriteAtomic("seen-words.json", seen.Values.OrderBy(x => x.Word, StringComparer.Ordinal).ToArray()); dirtySeen = false; }
        if (dirtyLearned)
        { WriteAtomic("learned-dictionary.json", learned.Values.OrderBy(x => x.Word, StringComparer.Ordinal).ToArray()); dirtyLearned = false; }
    }
    internal void RecordFallback(string word, string pinyin, string reason)
    {
        var normalized = WordMatching.Normalize(word);
        fallbacks.TryGetValue(normalized, out var old);
        fallbacks[normalized] = new(normalized, pinyin, reason, DateTimeOffset.UtcNow, (old?.Count ?? 0) + 1);
        dirtyFallbacks = true;
        Flush(); // Persist before clearing the input and starting character fallback.
    }
    internal void LearnComposition(DictionaryEntry entry)
    {
        if (!ValidEntry(entry) || effective.Keys.Any(w => WordMatching.Same(w, entry.Word))) return;
        learned[entry.Word] = entry; effective[entry.Word] = entry; dirtyLearned = true;
        Flush();
    }
    private void WriteAtomic<T>(string name, T data)
    {
        var path = Path.Combine(folder, name);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(data, JsonOptions), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
    private sealed record Pending(string Path, string Word, string Roman, DateTimeOffset At);
}
