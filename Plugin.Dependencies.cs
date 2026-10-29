using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace MakoMaster;

public sealed partial class Plugin
{
    private DependencyStatus GetDependencyStatus(string internalName)
    {
        var plugins = pi.InstalledPlugins.Where(p => string.Equals(p.InternalName, internalName, StringComparison.OrdinalIgnoreCase)).ToArray();
        return DependencyRules.Resolve(plugins.Length > 0, plugins.Any(p => p.IsLoaded));
    }
    private string? GetDependencyError(bool navigation, bool teleport) => DependencyRules.Missing(navigation, teleport,
        GetDependencyStatus(DependencyRules.NavigationPlugin), GetDependencyStatus(DependencyRules.TeleportPlugin));
    private string? GetTravelDependencyError() => GetDependencyError(true, DependencyRules.NeedsTeleport(clientState.TerritoryType));
    private bool dependencyGameInProgress;
    private long nextDependencySceneCheck;
    private void UpdateDependencyScene(long now)
    {
        if (now < nextDependencySceneCheck) return;
        nextDependencySceneCheck = now + 250;
        dependencyGameInProgress = IsMinigameInProgress();
    }
    private string? GetStartDependencyError(bool readCurrentScene = false)
    {
        if (selectedMode is not (RunMode.Solo or RunMode.Leader)) return null;
        return DependencyRules.ModeNeedsTravel(selectedMode, readCurrentScene ? IsMinigameInProgress() : dependencyGameInProgress)
            ? GetTravelDependencyError() == null ? null : DependencyRules.ManualNpcTooltip : null;
    }
    private void DrawDependencyStates()
    {
        ImGui.Separator();
        ImGui.TextUnformatted("依赖插件");
        foreach (var plugin in new[] { (Id: DependencyRules.TeleportPlugin, Purpose: "传送"), (Id: DependencyRules.NavigationPlugin, Purpose: "寻路") })
        {
            var status = GetDependencyStatus(plugin.Id);
            var color = status switch
            {
                DependencyStatus.Enabled => new Vector4(.25f, .85f, .35f, 1),
                DependencyStatus.Disabled => new Vector4(1, .8f, .2f, 1),
                _ => new Vector4(1, .3f, .3f, 1),
            };
            ImGui.TextUnformatted($"{plugin.Id}（{plugin.Purpose}）");
            ImGui.SameLine();
            ImGui.TextColored(color, DependencyRules.Label(status));
        }
    }
    private static void DependencyTooltip(string? reason)
    {
        if (reason != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(reason);
    }
}
