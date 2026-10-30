namespace MakoMaster;
internal sealed record DictionaryEntry(string Word, string[] Syllables, string Pinyin, string Segmented);
internal sealed record TrialWord(string Path, string Text, double DistanceSquared = 0, int Priority = 0, string Skill = "", bool Obscured = false);
internal sealed record TrialView(bool Ready, string Buffer, IReadOnlyList<TrialWord> Words, IReadOnlyDictionary<int, string> Candidates, string CandidatePage = "", int TotalPages = 0)
{
    internal static TrialView FromGameWindows(string? buffer, bool wordsWindowPresent, IReadOnlyList<TrialWord> words, IReadOnlyDictionary<int, string> candidates, string candidatePage = "", int totalPages = 0)
        => new(buffer != null && wordsWindowPresent, buffer ?? "", words, candidates, candidatePage, totalPages);
}
// Locked words survive visibility changes. Actions: PageDown '\f', Backspace '\b', send '\n'. No Escape is synthesized.
internal sealed class TrialMachine
{
    private readonly IReadOnlyDictionary<string, DictionaryEntry> dictionary;
    private readonly Random random;
    private readonly bool continuous;
    private readonly bool imeDictionaryInstalled;
    private readonly TimingSettings timings;
    private readonly Dictionary<string, long> skipUntil = new();
    private readonly Dictionary<string, string> submitted = new();
    private string targetPath = "";
    private int targetPriority;
    private Stage stage;
    private string word = "", normalizedWord = "", spelling = "", expected = "", prefix = "", desired = "";
    private string pageBefore = "", clearBefore = "", expectedCommit = "";
    private long nextActionAt, changedAt, nextKeyAllowedAt;
    private int sent, turns, unchangedPages, characterIndex;
    private bool characterMode, retainedComposition, inferredWhole;
    private string retainedTail = "";
    private string[][] readings = [];
    private int[] readingIndexes = [];
    private readonly HashSet<string> visitedPages = new();
    private ClearDestination clearDestination;
    internal int CompletedCount { get; private set; } // submissions, not verified kills
    internal bool HasEnteredGame { get; private set; }
    internal bool Started => stage is not (Stage.Idle or Stage.Done);
    internal bool Finished => stage == Stage.Done;
    internal string Result { get; private set; } = "等待小游戏与题目";
    internal string Target => word;
    internal string TargetPath => targetPath;
    internal string Phase => stage.ToString();
    internal string TargetEligibility(TrialWord target, long now)
    {
        var key = WordMatching.Normalize(target.Text);
        if (submitted.TryGetValue(target.Path, out var sentWord) && sentWord == key) return "已发送，不重试";
        if (skipUntil.TryGetValue(key, out var until) && until > now) return "暂缓：缺少读音或候选未找到";
        return "可选";
    }
    private void RecordMissingReading(string text, string[][] options, long now)
    {
        var key = WordMatching.Normalize(text);
        var missing = string.Concat(key.Where((c, i) => i >= options.Length || options[i].Length == 0).Distinct());
        skipUntil[key] = now + 15000;
        Fallbacks.Enqueue((key, "", "缺少读音，无法输入：" + missing));
    }
    internal void ExtendWait(long milliseconds) { nextActionAt += milliseconds; changedAt += milliseconds; nextKeyAllowedAt += milliseconds; }
    internal Queue<(string Word, string Pinyin, string Reason)> Fallbacks { get; } = new();
    internal Queue<DictionaryEntry> ComposedEntries { get; } = new();
    internal TrialMachine(IReadOnlyDictionary<string, DictionaryEntry> dictionary, long now, Random? timingRandom = null, bool continuous = false, TimingSettings? timings = null, bool imeDictionaryInstalled = true)
    {
        this.dictionary = dictionary; random = timingRandom ?? Random.Shared;
        this.continuous = continuous; this.timings = timings ?? new();
        this.imeDictionaryInstalled = imeDictionaryInstalled;
        if (this.timings.ValidationError != null) throw new ArgumentException(this.timings.ValidationError);
        nextActionAt = changedAt = now;
    }
    internal void PauseForScene(string reason, long now)
    {
        if (Finished) return;
        stage = Stage.Idle; sent = 0; expected = ""; nextActionAt = Math.Max(nextActionAt, now); Result = reason;
    }
    internal char? Tick(TrialView view, long now)
    {
        if (Finished) return null;
        if (!view.Ready) { PauseForScene("等待下一关 / 小游戏输入界面", now); return null; }
        if (now < nextKeyAllowedAt) return null;
        var key = TickCore(view, now);
        if (key != null) nextKeyAllowedAt = Math.Max(now + timings.KeyMinMs, nextActionAt);
        return key;
    }
    private char? TickCore(TrialView view, long now)
    {
        if (Finished) return null;
        if (!view.Ready) { PauseForScene("等待下一关 / 小游戏输入界面", now); return null; }

        var buffer = WordMatching.InputBuffer(view.Buffer);
        foreach (var path in submitted.Where(p => !view.Words.Any(w => w.Path == p.Key && WordMatching.Same(w.Text, p.Value))).Select(p => p.Key).ToArray())
            submitted.Remove(path);
        if (now < nextActionAt) return null;
        if (stage is Stage.Typing or Stage.Search or Stage.PageWait or Stage.ChooseWait)
        {
            var current = view.Words.FirstOrDefault(w => w.Path == targetPath && WordMatching.Same(w.Text, word));
            if (current != null) targetPriority = current.Priority;
            foreach (var urgent in WordPriority.Order(view.Words.Where(w => w.Priority > targetPriority)))
            {
                var key = WordMatching.Normalize(urgent.Text);
                if (submitted.TryGetValue(urgent.Path, out var sentWord) && sentWord == key) continue;
                if (skipUntil.TryGetValue(key, out var until) && until > now) continue;
                var entry = FindEntry(urgent.Text);
                var options = ReadingsFor(urgent.Text, entry);
                if (options.Length == 0 || options.Any(r => r.Length == 0)) { RecordMissingReading(urgent.Text, options, now); continue; }
                BeginClear(ClearDestination.Priority, now, "更高优先级目标：" + urgent.Text + "，清空后优先处理");
                break;
            }
        }
        if (now < nextActionAt) return null;
        if (stage == Stage.Idle)
        {
            if (view.Words.Count == 0) { Result = "等待题目"; return null; }
            if (view.Buffer.Length > 0) BeginClear(ClearDestination.Idle, now, "清空残留输入");
            else
            {
                DictionaryEntry? entry = null; TrialWord? selected = null;
                foreach (var target in WordPriority.Order(view.Words))
                {
                    var key = WordMatching.Normalize(target.Text);
                    if (submitted.TryGetValue(target.Path, out var sentWord) && sentWord == key) continue;
                    if (skipUntil.TryGetValue(key, out var until) && until > now) continue;
                    entry = FindEntry(target.Text);
                    var options = ReadingsFor(target.Text, entry);
                    if (options.Length == 0 || ((entry == null || !imeDictionaryInstalled) && options.Any(x => x.Length == 0)))
                    { RecordMissingReading(target.Text, options, now); continue; }
                    selected = target; readings = options; break;
                }
                if (selected == null) { Result = "等待新题目（已发送题目不重试，缺词已记录）"; return null; }
                word = selected.Text; normalizedWord = WordMatching.Normalize(word);
                targetPath = selected.Path; targetPriority = selected.Priority;
                readingIndexes = new int[readings.Length]; characterIndex = 0;
                // Keep dictionary syllables as pronunciation data even without an imported IME word list.
                characterMode = !imeDictionaryInstalled;
                inferredWhole = entry == null;
                spelling = entry?.Pinyin ?? string.Concat(readings.Select(r => r[0]));
                if (entry == null) Fallbacks.Enqueue((normalizedWord, "", "词库无整词，使用已知字音输入"));
                HasEnteredGame = true; PrepareUnit(now);
            }
        }
        if (stage == Stage.Typing)
        {
            if (!string.Equals(buffer, expected, StringComparison.OrdinalIgnoreCase))
            {
                if (now - changedAt > 2000 + timings.KeyMaxMs)
                {
                    if (characterMode) BeginClear(ClearDestination.Idle, now, "输入反馈不符，重建输入");
                    else Result = "保留拼音，等待输入反馈恢复";
                }
                return null;
            }
            if (sent > 0)
            {
                var early = FindCandidate(view);
                if (early.Key is >= 1 and <= 9) return Choose(early.Key, early.Value, now);
            }
            if (sent < spelling.Length)
            {
                var key = spelling[sent++]; expected += key;
                changedAt = now; nextActionAt = now + timings.NextKey(random); return key;
            }
            stage = Stage.Search; nextActionAt = Math.Max(now, changedAt + 250);
            Result = characterMode ? $"逐字找候选：{desired}" : $"寻找整词候选：{word}";
            if (now < nextActionAt) return null;
        }
        if (stage == Stage.Search)
        {
            var candidate = FindCandidate(view);
            if (candidate.Key is >= 1 and <= 9) return Choose(candidate.Key, candidate.Value, now);
            if (characterMode)
            {
                var maxTurns = Math.Clamp(view.TotalPages > 0 ? view.TotalPages : 64, 1, 256);
                if (turns >= maxTurns || unchangedPages >= 2) { NextReadingOrSkip(now); return null; }
            }
            pageBefore = Fingerprint(view);
            if (characterMode && !visitedPages.Add(pageBefore) && turns > 0) { NextReadingOrSkip(now); return null; }
            turns++; stage = Stage.PageWait; changedAt = now;
            nextActionAt = now + Math.Max(150, timings.NextKey(random));
            Result = characterMode ? $"逐字查找 {desired}，翻页 {turns}"
                : $"保留整串拼音，查找 {normalizedWord[characterIndex]}，翻页 {turns}";
            return '\f';
        }
        if (stage == Stage.PageWait)
        {
            if (Fingerprint(view) != pageBefore) { unchangedPages = 0; stage = Stage.Search; nextActionAt = now + 100; return null; }
            if (now - changedAt >= 800) { unchangedPages++; stage = Stage.Search; }
            return null;
        }
        if (stage == Stage.ChooseWait)
        {
            if (retainedComposition)
            {
                if (string.Equals(buffer, expectedCommit + retainedTail, StringComparison.OrdinalIgnoreCase))
                {
                    characterIndex = expectedCommit.Length;
                    if (characterIndex >= normalizedWord.Length) return Submit(now);
                    prefix = expectedCommit; desired = normalizedWord[characterIndex..];
                    expected = buffer;
                    turns = unchangedPages = 0; visitedPages.Clear();
                    stage = Stage.Search; nextActionAt = now + Math.Max(200, timings.NextKey(random));
                    Result = $"保留剩余拼音，继续选字 {characterIndex + 1}/{normalizedWord.Length}：{word}";
                }
                else if (buffer.Length == 0 && !view.Words.Any(w => WordMatching.Same(w.Text, word)))
                    return Submit(now, learn: false);
                else if (now - changedAt > 2000 + timings.KeyMaxMs)
                    Result = "保留拼音，等待选字反馈";
                return null;
            }
            if (WordMatching.Same(buffer, expectedCommit))
            {
                if (characterMode && ++characterIndex < normalizedWord.Length) { PrepareUnit(now); return null; }
                return Submit(now);
            }
            if (buffer.Length == 0 && !view.Words.Any(w => WordMatching.Same(w.Text, word)))
            {
                if (!characterMode || characterIndex >= normalizedWord.Length - 1) return Submit(now);
                BeginClear(ClearDestination.Characters, now, "目标已变化，按记忆重建整词后发送");
                return null;
            }
            if (now - changedAt > 2000 + timings.KeyMaxMs)
            {
                if (!characterMode) Result = "保留拼音，等待选词反馈"; else NextReadingOrSkip(now);
            }
            return null;
        }
        if (stage == Stage.AfterSubmit)
        {
            if (buffer.Length == 0) { FinishAttempt(now); return null; }
            if (now - changedAt > 1000) BeginClear(ClearDestination.Idle, now, "发送后清空残留，继续下一词");
            return null;
        }
        if (stage == Stage.ClearWait)
        {
            if (view.Buffer != clearBefore) stage = Stage.Clear;
            else if (now - changedAt > 1500) { stage = Stage.Clear; nextActionAt = now + 500; }
            return null;
        }
        if (stage == Stage.Clear)
        {
            if (view.Buffer.Length == 0)
            {
                if (clearDestination == ClearDestination.Characters) { characterMode = true; characterIndex = 0; PrepareUnit(now); }
                else { stage = Stage.Idle; nextActionAt = now + timings.NextWord(random); }
                return null;
            }
            clearBefore = view.Buffer; stage = Stage.ClearWait; changedAt = now; nextActionAt = now + timings.NextKey(random); return '\b';
        }
        return null;
    }
    private char Choose(int key, string text, long now)
    {
        expectedCommit = prefix + desired;
        retainedTail = retainedComposition
            ? string.Concat(readings.Skip(expectedCommit.Length).Select((r, offset) => r[readingIndexes[expectedCommit.Length + offset]])) : "";
        stage = Stage.ChooseWait; changedAt = now;
        nextActionAt = now + Math.Max(200, timings.NextKey(random)); Result = $"选词 {key}：{text}"; return (char)('0' + key);
    }
    private char Submit(long now, bool learn = true)
    {
        if (learn && (characterMode || retainedComposition || inferredWhole) && readings.All(r => r.Length > 0))
        {
            var syllables = readings.Select((r, i) => r[readingIndexes[i]]).ToArray();
            ComposedEntries.Enqueue(new(normalizedWord, syllables, string.Concat(syllables), string.Join("'", syllables)));
        }
        submitted[targetPath] = normalizedWord;
        CompletedCount++; stage = Stage.AfterSubmit; changedAt = now; nextActionAt = now + Math.Max(250, timings.NextKey(random));
        Result = $"发送：{word}（累计 {CompletedCount} 词）"; return '\n';
    }
    private void FinishAttempt(long now)
    {
        if (!continuous) { Stop("单题已发送"); return; }
        stage = Stage.Idle; nextActionAt = now + timings.NextWord(random); Result = "等待下一词";
    }
    private void BeginClear(ClearDestination destination, long now, string reason) { clearDestination = destination; stage = Stage.Clear; nextActionAt = now; Result = reason; }
    private void PrepareUnit(long now)
    {
        retainedComposition = false; retainedTail = "";
        if (characterMode)
        {
            if (readings[characterIndex].Length == 0)
            { skipUntil[normalizedWord] = now + 15000; BeginClear(ClearDestination.Idle, now, "缺少该字读音，已记录并暂时换词"); return; }
            prefix = normalizedWord[..characterIndex]; desired = normalizedWord[characterIndex].ToString(); spelling = readings[characterIndex][readingIndexes[characterIndex]];
        }
        else { prefix = ""; desired = normalizedWord; }
        expected = prefix; sent = turns = unchangedPages = 0; visitedPages.Clear(); stage = Stage.Typing; changedAt = nextActionAt = now;
        Result = characterMode ? $"逐字输入 {characterIndex + 1}/{normalizedWord.Length}：{word}" : "输入整词：" + word;
    }
    private KeyValuePair<int,string> FindCandidate(TrialView view)
    {
        if (!characterMode)
        {
            var remaining = normalizedWord[characterIndex..];
            var full = view.Candidates.FirstOrDefault(p => WordMatching.Same(p.Value, remaining));
            if (full.Key is >= 1 and <= 9) { desired = remaining; return full; }
            // Do not commit an individual character until the entire spelling has been entered.
            if (sent < spelling.Length || readings.Any(r => r.Length == 0)) return default;
            var single = view.Candidates.FirstOrDefault(p => WordMatching.Same(p.Value, normalizedWord[characterIndex].ToString()));
            if (single.Key is >= 1 and <= 9)
            {
                retainedComposition = true;
                prefix = normalizedWord[..characterIndex]; desired = normalizedWord[characterIndex].ToString();
                return single;
            }
            return default;
        }
        return view.Candidates.FirstOrDefault(p => WordMatching.Same(p.Value, desired));
    }
    private void NextReadingOrSkip(long now)
    {
        if (readingIndexes[characterIndex] + 1 < readings[characterIndex].Length)
        { readingIndexes[characterIndex]++; BeginClear(ClearDestination.Characters, now, "更换读音后逐字重试"); }
        else { skipUntil[normalizedWord] = now + 15000; BeginClear(ClearDestination.Idle, now, "当前字候选未找到，清空后处理其他题目"); }
    }
    private DictionaryEntry? FindEntry(string text)
    {
        var key = WordMatching.Normalize(text);
        return dictionary.TryGetValue(key, out var exact) ? exact : dictionary.Values.FirstOrDefault(e => WordMatching.Same(e.Word, key));
    }
    private string[][] ReadingsFor(string text, DictionaryEntry? entry)
    {
        var normalized = WordMatching.Normalize(text);
        if (normalized.Length == 0) return [];
        if (entry != null && entry.Syllables.Length == normalized.Length) return entry.Syllables.Select(p => ValidPinyin(p) ? new[] { p } : Array.Empty<string>()).ToArray();
        return normalized.Select(character => dictionary.Values.SelectMany(e =>
        {
            var w = WordMatching.Normalize(e.Word);
            return w.Length == e.Syllables.Length ? w.Select((c, i) => (c, Py: e.Syllables[i])) : [];
        }).Where(pair => pair.c == character && ValidPinyin(pair.Py)).GroupBy(pair => pair.Py).OrderByDescending(g => g.Count()).Select(g => g.Key).ToArray()).ToArray();
    }
    private static bool ValidPinyin(string pinyin) => pinyin.Length > 0 && pinyin.All(c => c is >= 'a' and <= 'z');
    private static string Fingerprint(TrialView view) => view.CandidatePage + "|" + string.Join("|", view.Candidates.OrderBy(p => p.Key).Select(p => p.Key + ":" + p.Value));
    internal void Stop(string reason) { stage = Stage.Done; Result = reason; }
    private enum Stage { Idle, Typing, Search, PageWait, ChooseWait, Clear, ClearWait, AfterSubmit, Done }
    private enum ClearDestination { Idle, Characters, Priority }
}
