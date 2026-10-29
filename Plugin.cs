using System.Text;
using System.Text.Json;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MakoMaster;

// Native UI pointers stay on the framework thread and are never retained.
// Input is disabled unless the user explicitly arms auto or the one-question test.
public sealed partial class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commands;
    private readonly IChatGui chat;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private bool disposed;
    private CaptureSession? capture;
    private long captureUntil;
    private long nextSample;
    private readonly Dictionary<string, string> previous = new();
    private string lastCapture = "";
    private TrialMachine? trial;
    private readonly NativeInput keyboard = new();
    private long trialEarliest, nextTrialSample;
    private string lastTrialState = "";
    private string lastWaitPhase = "";
    private bool continuousMode;
    private VocabularyStore? vocabulary;
    private long nextVocabularySample, nextVocabularyFlush;
    private bool showControlWindow = true;
    private string lastRunResult = "尚未启动";
    private TimingSettings timingSettings = new();
    private int keyMinMs = 40, keyMaxMs = 100, wordMinMs = 800, wordMaxMs = 1600;
    private string timingMessage = "";
    private readonly WordResolver wordResolver = new();
    private readonly Queue<object> captureBacklog = new();
    private bool saveLogs;
    private string TimingFile => Path.Combine(pi.AssemblyLocation.DirectoryName!, "timing-settings.json");

    public Plugin(IDalamudPluginInterface pi, ICommandManager commands,
        IChatGui chat, IFramework framework, IPluginLog log, IObjectTable objects,
        ITargetManager targets, IClientState clientState, IAddonLifecycle addonLifecycle, IDataManager dataManager)
    {
        this.pi = pi; this.commands = commands; this.chat = chat;
        this.framework = framework; this.log = log;
        this.objects = objects; this.targetManager = targets; this.clientState = clientState;
        this.addonLifecycle = addonLifecycle;
        this.dataManager = dataManager;
        InitializeDiagnostics();
        try
        {
            timingSettings = TimingSettings.Load(TimingFile);
            keyMinMs = timingSettings.KeyMinMs; keyMaxMs = timingSettings.KeyMaxMs;
            wordMinMs = timingSettings.WordMinMs; wordMaxMs = timingSettings.WordMaxMs;
        }
        catch (Exception ex) { timingMessage = "读取设置失败，暂用默认值。"; log.Error(ex, "MakoMaster timing settings load failed"); }
        try { vocabulary = new VocabularyStore(pi.AssemblyLocation.DirectoryName!); }
        catch (Exception ex) { log.Error(ex, "MakoMaster vocabulary load failed; existing files preserved"); }
        commands.AddHandler("/makomaster", new CommandInfo(OnCommand)
        {
            HelpMessage = "打开控制窗口；ui：显示窗口；auto：连续输入；test：单题；start：采集；status/list/find/dump。使用窗口停止按钮，不注册 F8/Esc 停止键。",
        });
        pi.UiBuilder.DisableAutomaticUiHide = true;
        pi.UiBuilder.Draw += DrawControlWindow;
        pi.UiBuilder.OpenMainUi += ShowControlWindow;
        pi.UiBuilder.OpenConfigUi += ShowControlWindow;
        framework.Update += OnUpdate;
    }

    private void ShowControlWindow() => showControlWindow = true;

    private void DrawControlWindow()
    {
        if (disposed || !showControlWindow) return;
        ImGui.SetNextWindowSize(new Vector2(320, 0), ImGuiCond.FirstUseEver);
        var draw = ImGui.Begin("魔光键影控制##MakoMaster", ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoNavInputs
            | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoFocusOnAppearing);
        try
        {
            if (!draw) return;
            ImGui.TextUnformatted(trial == null ? "已停止" : $"已启动  ·  已发送 {trial.CompletedCount} 词");
            if (trial != null || lastRunResult != "尚未启动") ImGui.TextWrapped(trial?.Result ?? lastRunResult);
            if (roundLoop != null) ImGui.TextWrapped(roundLoop.Status);
            if (skillPaused) ImGui.TextWrapped("运动体探知干扰器 / 诅咒视线：读条结束前 1 秒至结束后 2.5 秒暂停");
            if (loopMessage.Length > 0) ImGui.TextWrapped(loopMessage);
            if (inputEnvironmentMessage.Length > 0) ImGui.TextWrapped(inputEnvironmentMessage);
            ImGui.Separator();
            if (ImGui.CollapsingHeader("高级设置"))
            {
                ImGui.BeginDisabled(trial != null);
                ImGui.TextUnformatted("按键随机间隔（毫秒）");
                ImGui.SetNextItemWidth(125);
                ImGui.InputInt("##keyMin", ref keyMinMs, 0, 0);
                ImGui.SameLine();
                ImGui.TextUnformatted("～"); ImGui.SameLine();
                ImGui.SetNextItemWidth(125);
                ImGui.InputInt("##keyMax", ref keyMaxMs, 0, 0);
                ImGui.TextUnformatted("每题随机停顿（毫秒）");
                ImGui.SetNextItemWidth(125);
                ImGui.InputInt("##wordMin", ref wordMinMs, 0, 0);
                ImGui.SameLine();
                ImGui.TextUnformatted("～"); ImGui.SameLine();
                ImGui.SetNextItemWidth(125);
                ImGui.InputInt("##wordMax", ref wordMaxMs, 0, 0);
                if (ImGui.Button("保存设置")) OnCommand("", "save-timing");
                if (timingMessage.Length > 0) ImGui.TextWrapped(timingMessage);
                if (ImGui.Button("恢复默认间隔")) ApplyTimingPreset(new TimingSettings());
                ImGui.SameLine();
                if (ImGui.Button("预设：快速")) ApplyTimingPreset(TimingSettings.FastPreset);
                ImGui.EndDisabled();
                if (ImGui.Button(saveLogs ? "关闭日志" : "保存日志")) OnCommand("", "toggle-logs");
                ImGui.SameLine(); ImGui.TextUnformatted(saveLogs ? "已开启" : "已关闭");
            }
            ImGui.BeginDisabled(trial != null);
            ImGui.Checkbox("循环开局", ref loopEnabled);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            ImGui.InputInt("##loopCount", ref loopCount, 0, 0);
            ImGui.SameLine(); ImGui.TextUnformatted("次后停止");
            if (loopEnabled)
            {
                ImGui.TextWrapped("自动选择入场 NPC，靠近后自动开始；每次成绩结算计为一局。");
            }
            ImGui.EndDisabled();
            ImGui.Separator();
            ImGui.BeginDisabled(trial != null || OpeningChests);
            if (ImGui.Button("开始", new Vector2(135, 36))) OnCommand("", loopEnabled ? "loop" : "auto");
            ImGui.EndDisabled(); ImGui.SameLine();
            ImGui.BeginDisabled(trial == null && capture == null && !OpeningChests);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.65f, 0.16f, 0.16f, 1));
            if (ImGui.Button("停止", new Vector2(135, 36))) OnCommand("", "stop");
            ImGui.PopStyleColor();
            ImGui.EndDisabled();
            DrawChestControls();
            if (trial == null && capture == null && !OpeningChests && ImGui.Button("隐藏窗口")) showControlWindow = false;
        }
        finally { ImGui.End(); }
    }

    private void ApplyTimingPreset(TimingSettings preset)
    {
        keyMinMs = preset.KeyMinMs; keyMaxMs = preset.KeyMaxMs;
        wordMinMs = preset.WordMinMs; wordMaxMs = preset.WordMaxMs;
        timingMessage = "已应用，启动时生效；未保存文件。";
    }
    private void SaveTimingSettings(bool persist = true)
    {
        var updated = new TimingSettings(keyMinMs, keyMaxMs, wordMinMs, wordMaxMs);
        if (updated.ValidationError != null) { timingMessage = updated.ValidationError; throw new InvalidDataException(timingMessage); }
        if (persist && (updated != timingSettings || !File.Exists(TimingFile))) updated.Save(TimingFile);
        timingSettings = updated;
        timingMessage = persist ? "已保存，启动时生效。" : "本次运行生效，未保存文件。";
    }

    private void OnCommand(string command, string args)
    {
        // Defer until the chat command has finished executing.
        framework.RunOnFrameworkThread(() =>
        {
            if (disposed) return;
            try { Execute(args.Trim()); }
            catch (Exception ex)
            {
                lastRunResult = "操作失败：" + ex.Message;
                log.Error(ex, "MakoMaster command failed"); chat.PrintError("[MakoMaster] " + lastRunResult);
            }
        });
    }

    private unsafe void Execute(string args)
    {
        var split = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (split.Length == 0 || split[0] == "ui") { ShowControlWindow(); return; }
        if (split[0] == "open-chests") { StartChestOpening(); return; }
        if (split[0] == "stop-chests") { StopChestOpening("手动停止开箱"); return; }
        if (split[0] == "toggle-logs") { SetLogSaving(!saveLogs); return; }
        if (split[0] == "save-timing") { if (trial == null) SaveTimingSettings(); return; }
        if (split[0] is "test" or "auto" or "loop")
        {
            if (trial != null) { ShowControlWindow(); chat.Print("[MakoMaster] 已等待或正在输入，请使用控制窗口的停止按钮。"); return; }
            if (OpeningChests) { chestMessage = "请先停止开箱，再启动自动打字或循环。"; return; }
            SaveTimingSettings(saveLogs);
            var dictionary = vocabulary?.EffectiveDictionary ?? throw new InvalidDataException("词库未能加载，详见卫月日志");
            if (dictionary.Count == 0) throw new InvalidDataException("词库为空");
            if (split[0] == "loop") PrepareLoop();
            else if (split[0] == "auto" && !IsMinigameInProgress()) PrepareLoop(1, exitExistingResult: true);
            else roundLoop = null;
            if (saveLogs && capture == null) Execute("start 180");
            continuousMode = split[0] != "test";
            captureUntil = continuousMode ? long.MaxValue : Math.Max(captureUntil, Environment.TickCount64 + 180000);
            ResetInputEnvironment();
            trial = new TrialMachine(dictionary, Environment.TickCount64, continuous: continuousMode, timings: timingSettings);
            trialEarliest = Environment.TickCount64 + 3000;
            nextTrialSample = 0;
            lastTrialState = "";
            lastWaitPhase = "";
            lastRunResult = "已启动";
            wordResolver.Reset();
            skillPause.Reset(); skillPaused = false; lastSkillPauseTick = 0;
            showControlWindow = true;
            WriteRecord(new { Kind = "InputMode", At = DateTimeOffset.Now, Continuous = continuousMode,
                TimingSettings = timingSettings,
                LoopCount = roundLoop?.Limit, Npc = loopNpcName, NpcDataId = loopNpcDataId,
                Priority = "green barrier > confirmed interruptible cast > other cast > normal; distance within tier", AnchorX = 0.5, AnchorY = 1.0 });
            chat.Print($"[MakoMaster] 词库 {dictionary.Count} 条。{(continuousMode ? "连续模式" : "一题试打")}已待命，三秒后开始检查；使用控制窗口按钮停止，F8/Esc 不再中断插件。");
            return;
        }
        if (split[0] == "off") { StopTrial("手动停止"); return; }
        if (split[0] == "start")
        {
            if (!saveLogs) { chat.Print("[MakoMaster] 请先在高级设置中点击“保存日志”。"); return; }
            if (capture != null) { chat.Print("[MakoMaster] 正在采集。先 stop 可保存并结束本次采集。"); return; }
            var seconds = 180;
            if (split.Length > 1 && (!int.TryParse(split[1], out seconds) || seconds < 10 || seconds > 600))
            { chat.PrintError("[MakoMaster] 采集时间应为 10～600 秒。"); return; }
            saveLogs = true;
            if (vocabulary != null) vocabulary.PersistenceEnabled = true;
            var sessionFolder = Path.Combine(pi.AssemblyLocation.DirectoryName!, "captures");
            lastCapture = Path.Combine(sessionFolder, $"session-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
            capture = new CaptureSession(lastCapture);
            previous.Clear();
            captureUntil = Environment.TickCount64 + seconds * 1000L;
            nextSample = 0;
            if (!WriteRecord(new { Kind = "Start", Version = typeof(Plugin).Assembly.GetName().Version?.ToString(), At = DateTimeOffset.Now,
                SampleIntervalMs = 250, DurationSeconds = seconds, DalamudAssembly = typeof(IDalamudPlugin).Assembly.GetName().Version?.ToString() }))
            { StopCapture("无法写入文件"); return; }
            chat.Print($"[MakoMaster] 后台采集已开始，{seconds} 秒后自动结束。现在关闭聊天框并进入小游戏，正常打几道题即可。");
            chat.Print("[MakoMaster] 文件：" + lastCapture);
            return;
        }
        if (split[0] == "stop") { StopChestOpening("手动停止"); StopTrial("手动停止"); SetLogSaving(false); return; }
        if (split[0] == "status")
        {
            if (vocabulary != null) chat.Print($"[MakoMaster] 已记录 {vocabulary.SeenCount} 种题目，新增学习 {vocabulary.LearnedCount} 条；可用词库 {vocabulary.EffectiveDictionary.Count} 条。");
            if (trial != null) chat.Print($"[MakoMaster] {(continuousMode ? "连续模式" : "单题模式")}，已完成 {trial.CompletedCount} 题；" + trial.Result);
            chat.Print(capture == null ? "[MakoMaster] 未在采集。最近文件：" + lastCapture
                : $"[MakoMaster] 采集中，{(captureUntil == long.MaxValue ? "持续至停止" : $"剩余 {Math.Max(0, (captureUntil - Environment.TickCount64) / 1000)} 秒")}；已入队 {capture.Frames} 条记录。文件：{lastCapture}");
            return;
        }
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null) { chat.PrintError("[MakoMaster] 游戏 UI 未就绪。"); return; }
        var results = new List<AddonSnapshot>();
        var names = new List<string>();
        var list = &manager->AllLoadedUnitsList;
        var query = split.Length > 1 ? split[1] : "";
        if (split[0] != "list" && split[0] != "find" && split[0] != "dump")
        { chat.PrintError("[MakoMaster] 使用 list、find 或 dump。"); return; }
        if (split[0] != "list" && query.Length == 0)
        { chat.PrintError("[MakoMaster] 请提供题目片段或窗口名。"); return; }
        for (var i = 0; i < Math.Min((int)list->Count, list->Entries.Length); i++)
        {
            var addon = list->Entries[i].Value;
            if (addon == null || !addon->IsVisible) continue;
            var nameBytes = addon->Name;
            var end = nameBytes.IndexOf((byte)0);
            var name = Encoding.UTF8.GetString(end < 0 ? nameBytes : nameBytes[..end]);
            names.Add(name);
            if (split[0] == "list") continue;
            // The command itself contains the searched text and appears in chat.
            if (split[0] == "find" && name.Contains("ChatLog", StringComparison.OrdinalIgnoreCase)) continue;
            if (split[0] == "dump" && name != query) continue;
            var rows = new List<TextRow>();
            var visited = new HashSet<nint>();
            Walk(&addon->UldManager, "", true, 0, visited, rows);
            var values = ReadValues(addon);
            // find only considers nodes whose ancestor chain is visible, plus
            // addon values; dump preserves hidden nodes for before/after analysis.
            if (split[0] == "find" && !rows.Any(r => r.Visible && r.Text.Contains(query, StringComparison.Ordinal))
                && !values.Any(v => v.Text.Contains(query, StringComparison.Ordinal))) continue;
            results.Add(new(name, addon->Id, rows, values));
        }
        if (split[0] == "list")
        {
            foreach (var name in names.Distinct().Order()) chat.Print("[MakoMaster] " + name);
            return;
        }
        if (results.Count == 0)
        { chat.Print("[MakoMaster] 未找到匹配。尝试较短片段，或用 list 查窗口名后 dump。"); return; }
        if (!saveLogs) { chat.Print("[MakoMaster] 请先在高级设置中点击“保存日志”，再导出诊断文件。"); return; }
        var folder = Path.Combine(pi.GetPluginConfigDirectory(), "captures");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"capture-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new
        {
            CapturedAt = DateTimeOffset.Now,
            DalamudAssembly = typeof(IDalamudPlugin).Assembly.GetName().Version?.ToString(),
            Addons = results,
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), Encoding.UTF8);
        foreach (var result in results) chat.Print($"[MakoMaster] {result.Name}: {result.Nodes.Count} 个文字节点，{result.Values.Count} 个窗口值。");
        chat.Print("[MakoMaster] 导出文件：" + file);
        log.Information("MakoMaster capture: {Path}", file);
    }

    private unsafe void OnUpdate(IFramework _)
    {
        if (disposed) return;
        UpdateVocabulary(Environment.TickCount64);
        try
        {
            UpdateChestOpening(Environment.TickCount64);
            keyboard.ReleaseDue(Environment.TickCount64);
            UpdateTrial(Environment.TickCount64);
        }
        catch (Exception ex) { log.Error(ex, "MakoMaster input paused"); PauseInput("输入暂时异常：" + ex.Message, Environment.TickCount64); }
        if (capture == null) return;
        if (capture.Error != null) { PauseInput("日志写入失败，保持待命，请检查磁盘", Environment.TickCount64); return; }
        while (captureBacklog.Count > 0 && capture.TryWrite(captureBacklog.Peek())) captureBacklog.Dequeue();
        if (captureBacklog.Count > 0) return;
        var now = Environment.TickCount64;
        if (now >= captureUntil) { SetLogSaving(false); return; }
        if (now < nextSample) return;
        nextSample = now + 250;
        try
        {
            var manager = RaptureAtkUnitManager.Instance();
            if (manager == null) return;
            var current = new Dictionary<string, string>();
            var changes = new List<AddonSnapshot>();
            var list = &manager->AllLoadedUnitsList;
            for (var i = 0; i < Math.Min((int)list->Count, list->Entries.Length); i++)
            {
                var addon = list->Entries[i].Value;
                if (addon == null || !addon->IsVisible) continue;
                var bytes = addon->Name;
                var end = bytes.IndexOf((byte)0);
                var name = Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]);
                if (name.Contains("ChatLog", StringComparison.OrdinalIgnoreCase)) continue;
                var rows = new List<TextRow>();
                Walk(&addon->UldManager, "", true, 0, new HashSet<nint>(), rows);
                var snapshot = new AddonSnapshot(name, addon->Id, rows, ReadValues(addon), ReadVisuals(addon, name));
                var key = name + "#" + addon->Id;
                var fingerprint = JsonSerializer.Serialize(snapshot, CaptureSession.JsonOptions);
                current[key] = fingerprint;
                if (!previous.TryGetValue(key, out var old) || fingerprint != old) changes.Add(snapshot);
            }
            var removed = previous.Keys.Where(key => !current.ContainsKey(key)).ToArray();
            if (changes.Count > 0 || removed.Length > 0)
            {
                if (!WriteRecord(new { Kind = "Changes", At = DateTimeOffset.Now, TickMs = now,
                    Addons = changes, HiddenOrClosed = removed }))
                { PauseInput("等待日志写盘", now); return; }
            }
            previous.Clear();
            foreach (var pair in current) previous[pair.Key] = pair.Value;
        }
        catch (Exception ex)
        { log.Error(ex, "MakoMaster automatic snapshot paused"); nextSample = now + 1000; PauseInput("界面暂时不可读，等待恢复", now); }
    }

    private void SetLogSaving(bool enabled)
    {
        if (enabled)
        {
            try
            {
                saveLogs = true;
                if (capture == null) Execute("start 600");
                captureUntil = long.MaxValue;
                if (vocabulary != null) { vocabulary.PersistenceEnabled = true; vocabulary.Flush(); }
                WriteRecord(new { Kind = "LoggingEnabled", At = DateTimeOffset.Now,
                    Running = trial != null, Continuous = continuousMode, TimingSettings = timingSettings });
            }
            catch
            {
                saveLogs = false;
                if (vocabulary != null) vocabulary.PersistenceEnabled = false;
                StopCapture("开启日志失败", stopInput: false);
                throw;
            }
        }
        else
        {
            try { vocabulary?.Flush(); }
            finally
            {
                if (vocabulary != null) vocabulary.PersistenceEnabled = false;
                saveLogs = false;
                if (capture != null) StopCapture("已关闭日志保存", stopInput: false);
            }
        }
    }

    private bool WriteRecord(object record)
    {
        if (!saveLogs || capture == null) return true;
        if (captureBacklog.Count > 0 || !capture.TryWrite(record)) captureBacklog.Enqueue(record);
        return true;
    }
    private void PauseInput(string reason, long now)
    {
        environmentPaused = false; environmentPauseAt = 0;
        trial?.PauseForScene(reason, now);
        lastRunResult = reason; nextTrialSample = now + 1000;
    }

    private void UpdateVocabulary(long now)
    {
        if (vocabulary == null || now < nextVocabularySample) return;
        nextVocabularySample = now + 100;
        try
        {
            var words = ReadNamedAddon("KTGEWord");
            var input = words == null ? null : ReadNamedAddon("KTGInput");
            var targets = words?.Nodes.Where(n => n.Visible && n.NodeId == 20 && n.Text.Length > 0)
                .Select(n => new TrialWord(n.Path, n.Text)).ToList() ?? [];
            var buffer = input?.Nodes.FirstOrDefault(n => n.Visible && n.Path == "/7[4]/16[4]")?.Text ?? "";
            vocabulary.Observe(new TrialView(input != null, buffer, targets, new Dictionary<int, string>()), DateTimeOffset.UtcNow);
            if (now >= nextVocabularyFlush)
            { vocabulary.Flush(); nextVocabularyFlush = now + 5000; }
        }
        catch (Exception ex)
        {
            log.Error(ex, "MakoMaster vocabulary persistence failed; input stopped and learning disabled");
            vocabulary = null;
            chat.PrintError("[MakoMaster] 词库记录失败，本次加载已停用学习；请检查文件或卫月日志。");
        }
    }

    private unsafe AddonSnapshot? ReadNamedAddon(string name)
    {
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null) return null;
        var list = &manager->AllLoadedUnitsList;
        for (var i = 0; i < Math.Min((int)list->Count, list->Entries.Length); i++)
        {
            var addon = list->Entries[i].Value;
            if (addon == null || !addon->IsVisible) continue;
            var bytes = addon->Name;
            var end = bytes.IndexOf((byte)0);
            if (Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]) != name) continue;
            var rows = new List<TextRow>();
            Walk(&addon->UldManager, "", true, 0, new HashSet<nint>(), rows);
            return new(name, addon->Id, rows, ReadValues(addon), name == "KTGEnemyCastBar" ? ReadVisuals(addon, name) : null);
        }
        return null;
    }

    private void UpdateTrial(long now)
    {
        if (trial == null) return;
        if (saveLogs && (capture == null || capture.Error != null || captureBacklog.Count > 0))
        { PauseInput("等待开发日志写入恢复", now); return; }
        if (!NativeInput.ForegroundIsGame)
        { PauseInput("失焦暂停，回到游戏后自动继续", now); return; }
        var blockReason = NativeInput.InputBlockReason;
        // Lobby movement and menu shortcuts must not cancel an armed trial.
        if (blockReason.Length > 0)
        { PauseInput("等待松开或关闭：" + blockReason, now); return; }
        if (now < trialEarliest || now < nextTrialSample) return;
        nextTrialSample = now + 10;
        if (!inputLanguage.Tick(now)) { HoldForInputEnvironment(inputLanguage.Status, now); return; }
        if (UpdateRoundLoop(now)) { ResumeInputEnvironment(now); return; }
        var input = ReadNamedAddon("KTGInput");
        var words = ReadNamedAddon("KTGEWord");
        var inResults = ReadNamedAddon("_KTGClear") != null || ReadNamedAddon("KTGResult") != null;
        if (words == null || input == null || inResults)
        {
            ResumeInputEnvironment(now);
            wordResolver.Reset();
            trial.Tick(new TrialView(false, "", [], new Dictionary<int,string>()), now);
            if (lastTrialState != trial.Result)
            { lastTrialState = trial.Result; WriteRecord(new { Kind = "SceneWait", At = DateTimeOffset.Now, State = trial.Result, ResultsVisible = inResults }); }
            return;
        }
        if (!MaintainInputFocus(now)) { HoldForInputEnvironment("正在恢复小游戏输入框焦点", now); return; }
        ResumeInputEnvironment(now);
        var casting = ReadNamedAddon("KTGEnemyCastBar");
        ObserveInterrupts(casting);
        if (UpdateSkillPause(casting, now) || keyboard.Holding) return;
        // The game hides this text node when the field is empty after Enter.
        // Its visibility and AtkValue[2] are not readiness signals. The live
        // minigame windows plus the existing buffer node gate input instead.
        var bufferNode = input?.Nodes.FirstOrDefault(n => n.Path == "/7[4]/16[4]");
        var candidates = new Dictionary<int, string>();
        foreach (var row in input?.Nodes.Where(n => n.Visible && n.Path.EndsWith("/2[2]")) ?? [])
        {
            var match = System.Text.RegularExpressions.Regex.Match(row.Text, @"^([1-9])\s*:\s*(.+)$");
            if (match.Success) candidates[int.Parse(match.Groups[1].Value)] = match.Groups[2].Value;
        }
        if (!NativeInput.TryGetClientSize(out var clientWidth, out var clientHeight))
        { PauseInput("等待游戏窗口恢复", now); return; }
        var skills = new Dictionary<string,string>();
        foreach (var row in casting?.Nodes.Where(n => n.Visible && n.NodeId == 4 && n.Text.Length > 0) ?? [])
            skills[WordMatching.Slot(row.Path)] = row.Text;
        var observations = words.Nodes.Where(n => n.NodeId == 20 && float.IsFinite(n.ScreenX) && float.IsFinite(n.ScreenY))
            .Select(n => new WordObservation(n.Path, n.Text, n.Visible, n.ContainerVisible,
                Math.Pow(n.ScreenX - clientWidth * 0.5, 2) + Math.Pow(n.ScreenY - clientHeight, 2), n.TextColor));
        var targets = wordResolver.Resolve(observations, vocabulary?.EffectiveDictionary ?? new Dictionary<string,DictionaryEntry>(), skills, interruptibleSkills, ReadCastKinds(casting));
        var page = input!.Nodes.FirstOrDefault(n => n.Path == "/7[4]/14[7]" && n.Visible)?.Text ?? "";
        var pageMatch = System.Text.RegularExpressions.Regex.Match(page, @"\((\d+)/(\d+)\)");
        var totalPages = pageMatch.Success ? int.Parse(pageMatch.Groups[2].Value) : 0;
        var view = TrialView.FromGameWindows(bufferNode?.Text, !inResults, targets, candidates, page, totalPages);
        if (!trial.Started && targets.Count == 0)
        {
            var phase = $"input={input != null}, words={words != null}, buffer={bufferNode != null}";
            if (phase != lastWaitPhase)
            {
                lastWaitPhase = phase;
                var message = input == null || words == null ? "等待进入小游戏"
                    : "小游戏界面已加载，等待题目出现（可能仍在开场或倒计时）";
                if (!WriteRecord(new { Kind = "TrialWait", At = DateTimeOffset.Now, State = message,
                    InputWindowPresent = input != null, WordsWindowPresent = words != null,
                    InputNodePresent = bufferNode != null, InputTextVisible = bufferNode?.Visible,
                    NativeInputFlag = input?.Values.FirstOrDefault(v => v.Index == 2)?.Text,
                    VisibleWordCount = targets.Count }))
                { PauseInput("等待日志写盘", now); return; }
                if (!trial.HasEnteredGame) chat.Print("[MakoMaster] " + message + "；控制窗口可停止。");
            }
        }
        if (!trial.Started && blockReason.Length > 0)
        {
            if (view.Ready && targets.Count > 0)
            {
                var waiting = "等待松开或关闭：" + blockReason;
                if (lastTrialState != waiting)
                {
                    lastTrialState = waiting;
                    if (!WriteRecord(new { Kind = "TrialGuard", At = DateTimeOffset.Now, State = waiting, View = view }))
                        PauseInput("等待日志写盘", now);
                }
            }
            return;
        }
        var key = trial.Tick(view, now);
        while (trial.Fallbacks.TryDequeue(out var fallback))
        {
            vocabulary?.RecordFallback(fallback.Word, fallback.Pinyin, fallback.Reason);
            WriteRecord(new { Kind = "WordFallback", At = DateTimeOffset.Now, fallback.Word, fallback.Pinyin, fallback.Reason });
        }
        while (trial.ComposedEntries.TryDequeue(out var entry))
        {
            vocabulary?.LearnComposition(entry);
            WriteRecord(new { Kind = "ComposedWord", At = DateTimeOffset.Now, Entry = entry });
        }
        if (key != null || lastTrialState != trial.Result)
        {
            lastTrialState = trial.Result;
            if (!WriteRecord(new { Kind = "Trial", At = DateTimeOffset.Now, State = trial.Result,
                Target = trial.Target, CompletedCount = trial.CompletedCount,
                InputTextVisible = bufferNode?.Visible,
                NativeInputFlag = input?.Values.FirstOrDefault(v => v.Index == 2)?.Text,
                Anchor = new { X = clientWidth * 0.5, Y = clientHeight },
                Key = key == '\n' ? "Enter" : key == '\f' ? "PageDown" : key == '\b' ? "Backspace" : key?.ToString(), View = view }))
            { PauseInput("等待日志写盘", now); return; }
            if (saveLogs) log.Information("MakoMaster input: {State}, key={Key}", trial.Result, key?.ToString() ?? "none");
        }
        if (trial.Finished) { StopTrial(trial.Result); return; }
        if (key != null) keyboard.Press(key.Value, now);
    }

    private void StopTrial(string reason)
    {
        if (trial == null && !keyboard.Holding) return;
        ResetInputEnvironment();
        var target = trial?.Target;
        var completed = trial?.CompletedCount ?? 0;
        var wasContinuous = continuousMode;
        roundLoop = null; loopMessage = ""; skillPause.Reset(); skillPaused = false; lastSkillPauseTick = 0;
        lastRunResult = reason;
        continuousMode = false;
        trial = null;
        try { keyboard.Release(); }
        catch (Exception ex) { log.Error(ex, "MakoMaster key release failed"); }
        capture?.TryWrite(new { Kind = "TrialEnd", At = DateTimeOffset.Now, Target = target,
            Continuous = wasContinuous, CompletedCount = completed, Reason = reason });
        if (wasContinuous && capture != null) captureUntil = Environment.TickCount64;
        if (!disposed) chat.Print($"[MakoMaster] 自动输入已停止，已完成 {completed} 题：" + reason);
    }

    private void StopCapture(string reason, bool stopInput = true)
    {
        if (stopInput) StopTrial("后台采集停止：" + reason);
        var session = capture;
        if (session == null) { if (!disposed) chat.Print("[MakoMaster] 当前未在采集。"); return; }
        capture = null;
        previous.Clear();
        var pending = captureBacklog.ToArray();
        captureBacklog.Clear();
        _ = FinalizeCapture(session, pending, reason);
        if (!disposed)
        {
            chat.Print("[MakoMaster] " + reason + "。记录已停止，后台正在完成文件写入。");
            chat.Print("[MakoMaster] 文件：" + session.FilePath);
        }
    }

    private async Task FinalizeCapture(CaptureSession session, object[] pending, string reason)
    {
        foreach (var record in pending.Append(new { Kind = "End", At = DateTimeOffset.Now, Reason = reason }))
        {
            while (session.Error == null && !session.TryWrite(record)) await Task.Delay(25);
            if (session.Error != null) break;
        }
        session.Complete();
        await ReportSaved(session);
    }

    private async Task ReportSaved(CaptureSession session)
    {
        await session.Completion;
        await framework.RunOnFrameworkThread(() =>
        {
            if (disposed) return;
            if (session.Error == null) chat.Print("[MakoMaster] 文件已保存，可以发送：" + session.FilePath);
            else { log.Error(session.Error, "MakoMaster capture incomplete"); chat.PrintError("[MakoMaster] 文件写入失败，数据可能不完整。"); }
        });
    }

    private static unsafe void Walk(AtkUldManager* uld, string prefix, bool parentVisible, int depth,
        HashSet<nint> visited, List<TextRow> rows)
    {
        if (uld == null || uld->NodeList == null || depth > 12) return;
        for (var i = 0; i < Math.Min((int)uld->NodeListCount, 4096); i++)
        {
            var node = uld->NodeList[i];
            if (node == null || visited.Count >= 16384 || !visited.Add((nint)node)) continue;
            var path = prefix + "/" + node->NodeId + "[" + i + "]";
            var containerVisible = parentVisible;
            var parent = node->ParentNode;
            var ancestors = 0;
            while (containerVisible && parent != null && ancestors++ < 64)
            { containerVisible &= parent->IsVisible(); parent = parent->ParentNode; }
            if (parent != null && ancestors >= 64) containerVisible = false;
            var visible = containerVisible && node->IsVisible();
            if (node->Type == NodeType.Text)
            {
                var textNode = (AtkTextNode*)node;
                var raw = textNode->NodeText.AsSpan();
                if (raw.Length > 32768) continue;
                var text = SeString.Parse(raw).TextValue;
                rows.Add(new(path, node->NodeId, visible, text, Convert.ToHexString(raw), textNode->TextColor.RGBA,
                    node->ScreenX, node->ScreenY, containerVisible));
            }
            else if ((int)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null) Walk(&component->UldManager, path, visible, depth + 1, visited, rows);
            }
        }
    }

    private static unsafe List<ValueRow> ReadValues(AtkUnitBase* addon)
    {
        var result = new List<ValueRow>();
        if (addon->AtkValues == null) return result;
        for (var i = 0; i < Math.Min((int)addon->AtkValuesCount, 4096); i++)
        {
            var value = addon->AtkValues + i;
            if (value->Type != AtkValueType.String && value->Type != AtkValueType.ManagedString
                && value->Type != AtkValueType.ConstString)
            {
                var scalar = value->Type switch
                {
                    AtkValueType.Bool => value->Bool.ToString(),
                    AtkValueType.Int => value->Int.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    AtkValueType.UInt => value->UInt.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    AtkValueType.Int64 => value->Int64.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    AtkValueType.UInt64 => value->UInt64.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    AtkValueType.Float => value->Float.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => "",
                };
                result.Add(new(i, value->Type.ToString(), scalar, ""));
                continue;
            }
            byte* ptr = value->String;
            if (ptr == null) continue;
            var length = 0;
            while (length < 32768 && ptr[length] != 0) length++;
            if (length == 32768) continue;
            var raw = new ReadOnlySpan<byte>(ptr, length);
            result.Add(new(i, value->Type.ToString(), SeString.Parse(raw).TextValue, Convert.ToHexString(raw)));
        }
        return result;
    }

    public void Dispose()
    {
        disposed = true;
        pi.UiBuilder.Draw -= DrawControlWindow;
        pi.UiBuilder.OpenMainUi -= ShowControlWindow;
        pi.UiBuilder.OpenConfigUi -= ShowControlWindow;
        framework.Update -= OnUpdate;
        DisposeDiagnostics();
        StopChestOpening("插件卸载");
        StopTrial("插件卸载");
        StopCapture("插件卸载");
        try { vocabulary?.Flush(); }
        catch (Exception ex) { log.Error(ex, "MakoMaster vocabulary flush failed"); }
        commands.RemoveHandler("/makomaster");
    }
    private sealed record AddonSnapshot(string Name, ushort Id, List<TextRow> Nodes, List<ValueRow> Values, List<VisualRow>? Visuals = null);
    private sealed record TextRow(string Path, uint NodeId, bool Visible, string Text, string RawHex, uint TextColor,
        float ScreenX, float ScreenY, bool ContainerVisible);
    private sealed record ValueRow(int Index, string Type, string Text, string RawHex);
}
