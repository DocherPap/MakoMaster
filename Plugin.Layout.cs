using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace MakoMaster;

public sealed partial class Plugin
{
    private const float ControlButtonWidth = 135;
    private static float ControlRowWidth => ControlButtonWidth * 2 + ImGui.GetStyle().ItemSpacing.X;
    private static float NumberInputWidth(int digits) => ImGui.CalcTextSize(new string('0', digits)).X + ImGui.GetStyle().FramePadding.X * 2;
    private void DrawTimingPresets()
    {
        var style = ImGui.GetStyle();
        var width = (ControlRowWidth - 8) / 3;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, style.ItemSpacing.Y));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, style.FramePadding.Y));
        try
        {
            if (ImGui.Button("预设：默认", new Vector2(width, 0))) ApplyTimingPreset(new TimingSettings());
            ImGui.SameLine();
            if (ImGui.Button("预设：快速", new Vector2(width, 0))) ApplyTimingPreset(TimingSettings.FastPreset);
            ImGui.SameLine();
            if (ImGui.Button("预设：迪迦", new Vector2(width, 0))) ApplyTimingPreset(TimingSettings.DigaPreset);
        }
        finally { ImGui.PopStyleVar(2); }
    }
}
