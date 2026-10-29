using Dalamud.Bindings.ImGui;

namespace MakoMaster;

public sealed partial class Plugin
{
    private RunMode selectedMode = RunMode.Solo;
    private RunMode activeMode;
    private int soloRounds = 1, leaderRounds = 1;

    private void SelectRunMode(RunMode mode)
    {
        selectedMode = mode;
        if (mode != RunMode.Chests) ApplyTimingPreset(RunModePolicy.Preset(mode));
    }
    private void DrawModeControls()
    {
        ImGui.BeginDisabled(trial != null || OpeningChests);
        if (ImGui.RadioButton("单人排本", selectedMode == RunMode.Solo)) SelectRunMode(RunMode.Solo);
        ImGui.SameLine(); ImGui.SetNextItemWidth(90); ImGui.InputInt("##soloRounds", ref soloRounds, 0, 0);
        ImGui.SameLine(); ImGui.TextUnformatted("次");
        if (ImGui.RadioButton("组队队长", selectedMode == RunMode.Leader)) SelectRunMode(RunMode.Leader);
        ImGui.SameLine(); ImGui.SetNextItemWidth(90); ImGui.InputInt("##leaderRounds", ref leaderRounds, 0, 0);
        ImGui.SameLine(); ImGui.TextUnformatted("次");
        if (ImGui.RadioButton("组队队员", selectedMode == RunMode.Member)) SelectRunMode(RunMode.Member);
        if (ImGui.RadioButton("开启宝箱", selectedMode == RunMode.Chests)) SelectRunMode(RunMode.Chests);
        ImGui.SameLine(); ImGui.SetNextItemWidth(90); ImGui.InputInt("##chestCount", ref chestCount, 0, 0);
        ImGui.SameLine(); ImGui.TextUnformatted("个");
        ImGui.TextWrapped("0代表一直运行，组队模式需要4人小队");
        ImGui.EndDisabled();
        if (selectedMode == RunMode.Chests) DrawChestControls();
        ImGui.Separator();
    }
}
