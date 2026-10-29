namespace MakoMaster;

internal enum DependencyStatus { NotInstalled, Disabled, Enabled }
internal static class DependencyRules
{
    internal const string ManualNpcTooltip = "缺少自动寻路功能，请手动前往npc";
    internal const string ManualPlazaMessage = "缺少寻路组件，请手动前往键斗广场";
    internal static bool CanRegisterWithoutTravel(bool inPlaza, bool registrationOpen, bool npcInteractable)
        => inPlaza && (registrationOpen || npcInteractable);
    internal const string TeleportPlugin = "Lifestream", NavigationPlugin = "vnavmesh";
    internal static DependencyStatus Resolve(bool installed, bool loaded)
        => !installed ? DependencyStatus.NotInstalled : loaded ? DependencyStatus.Enabled : DependencyStatus.Disabled;
    internal static string Label(DependencyStatus status) => status switch
    {
        DependencyStatus.Enabled => "已启用",
        DependencyStatus.Disabled => "未启用",
        _ => "未安装",
    };
    internal static string? Missing(bool needsNavigation, bool needsTeleport, DependencyStatus navigation, DependencyStatus teleport)
    {
        var missing = new List<string>();
        if (needsNavigation && navigation != DependencyStatus.Enabled) missing.Add($"vnavmesh（{Label(navigation)}）");
        if (needsTeleport && teleport != DependencyStatus.Enabled) missing.Add($"Lifestream（{Label(teleport)}）");
        return missing.Count == 0 ? null : "依赖不可用：" + string.Join("、", missing) + "。请先安装并启用对应插件。";
    }
    internal static bool NeedsTeleport(uint territory) => territory != WorldAutomationRules.Plaza && territory != WorldAutomationRules.GoldSaucer;
    internal static bool ModeNeedsTravel(RunMode mode, bool inGame) => !inGame && mode is RunMode.Solo or RunMode.Leader;
}
