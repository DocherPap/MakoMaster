namespace MakoMaster;

public sealed partial class Plugin
{
    private readonly AutoEntrySession autoEntrySession = new();
    private bool autoStartOnEntry = true;
    private long nextAutoEntryCheck;
    private uint autoEntryTerritory;
    private string AutoEntryFile => Path.Combine(pi.GetPluginConfigDirectory(), "auto-entry.json");

    private void UpdateAutoEntry(long now)
    {
        if (now < nextAutoEntryCheck) return;
        nextAutoEntryCheck = now + 200;
        var loading = !clientState.IsLoggedIn || objects.LocalPlayer == null || ReadNamedAddon("NowLoading") != null;
        // Addons can disappear between waves: absence alone never rearms or ends a session.
        var home = !loading && (ReadNamedAddon("KTGEnterDialogue") != null || FindEntryNpc() != null);
        var leftGame = !loading && autoEntryTerritory != 0 && clientState.TerritoryType != autoEntryTerritory;
        var ended = !loading && (ReadNamedAddon("KTGResult") != null || home || leftGame);
        var action = autoEntrySession.Tick(autoStartOnEntry, !loading && IsMinigameInProgress(), ended,
            trial != null || OpeningChests || WorldBusy || worldFault || vocabulary == null, canRearm: home || leftGame);
        if (action == AutoEntryAction.Stop)
        {
            StopTrial("本局自动输入结束");
            return;
        }
        if (action != AutoEntryAction.Start) return;
        try
        {
            SaveTimingSettings(persist: false);
            var dictionary = vocabulary!.EffectiveDictionary;
            if (dictionary.Count == 0) throw new InvalidDataException("词库为空");
            roundLoop = null; loopMessage = "";
            continuousMode = true;
            ResetInputEnvironment();
            trial = new TrialMachine(dictionary, now, continuous: true, timings: timingSettings,
                imeDictionaryInstalled: imeDictionaryInstalled);
            autoEntryTerritory = clientState.TerritoryType;
            trialEarliest = now + 3000; nextTrialSample = 0;
            lastTrialState = lastWaitPhase = "";
            lastRunResult = "进本自动启动，仅处理当前一局";
            wordResolver.Reset(); lastCastWordLinks = "";
            skillPause.Reset(); skillPaused = false; lastSkillPauseTick = 0;
            if (saveLogs && capture == null) Execute("start 600");
            if (capture != null) captureUntil = long.MaxValue;
            WriteRecord(new { Kind = "AutoEntryStart", At = DateTimeOffset.Now, TimingSettings = timingSettings });
            PrintChat("[MakoMaster] 检测到魔光键影，已自动启动，本局结算时停止。");
        }
        catch
        {
            autoEntrySession.SuppressCurrentGame();
            StopTrial("进本自动启动失败");
            throw;
        }
    }

    private static IEnumerable<string> LegacyVocabularyFolders(string installedFolder)
    {
        // Dalamud installations use MakoMaster/<version>; never enumerate unrelated plugin folders.
        var parent = Directory.GetParent(installedFolder);
        if (parent?.Name == "MakoMaster")
            foreach (var directory in Directory.EnumerateDirectories(parent.FullName))
                if (Version.TryParse(Path.GetFileName(directory), out _)) yield return directory;
    }
}
