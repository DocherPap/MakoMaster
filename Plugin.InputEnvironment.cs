using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MakoMaster;
public sealed partial class Plugin
{
    private readonly InputLanguageGuard inputLanguage = new(new WindowsInputLanguage());
    private string inputEnvironmentMessage = "";
    private bool environmentPaused;
    private long environmentPauseAt, nextFocusAttempt;

    private void HoldForInputEnvironment(string reason, long now)
    {
        if (environmentPaused) trial?.ExtendWait(Math.Max(0, now - environmentPauseAt));
        environmentPaused = true; environmentPauseAt = now;
        if (skillPaused) lastSkillPauseTick = now;
        keyboard.Release();
        SetInputEnvironmentMessage(reason);
    }
    private void ResumeInputEnvironment(long now)
    {
        if (environmentPaused) trial?.ExtendWait(Math.Max(0, now - environmentPauseAt));
        if (environmentPaused && skillPaused) lastSkillPauseTick = now;
        environmentPaused = false;
        SetInputEnvironmentMessage(inputLanguage.Status.Contains("未提供", StringComparison.Ordinal) ? inputLanguage.Status : "");
    }
    private void SetInputEnvironmentMessage(string message)
    {
        if (inputEnvironmentMessage == message) return;
        inputEnvironmentMessage = message;
        WriteRecord(new { Kind = "InputEnvironment", At = DateTimeOffset.Now, State = message });
    }
    private void ResetInputEnvironment()
    {
        inputLanguage.Reset(); inputEnvironmentMessage = "";
        environmentPaused = false; environmentPauseAt = nextFocusAttempt = 0;
    }

    // Resolve fresh native pointers on each framework callback; never retain UI pointers.
    private static unsafe AtkUnitBase* FindInputAddon()
    {
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null) return null;
        var list = &manager->AllLoadedUnitsList;
        for (var i = 0; i < Math.Min((int)list->Count, list->Entries.Length); i++)
        {
            var addon = list->Entries[i].Value;
            if (addon != null && addon->IsVisible && addon->NameString == "KTGInput") return addon;
        }
        return null;
    }
    private static unsafe bool WithinInput(AtkResNode* node, AtkResNode* root)
    {
        for (var i = 0; node != null && i < 64; i++, node = node->ParentNode)
            if (node == root) return true;
        return false;
    }
    private unsafe bool MaintainInputFocus(long now)
    {
        var addon = FindInputAddon();
        var manager = RaptureAtkUnitManager.Instance();
        var stage = AtkStage.Instance();
        if (addon == null || manager == null || stage == null) return false;
        var container = addon->GetNodeById(7);
        if (container == null || !container->IsVisible() || (int)container->Type < 1000) return false;
        var component = ((AtkComponentNode*)container)->Component;
        if (component == null) return false;
        var focus = component->GetFocusNode();
        if (focus == null) focus = container;
        bool Focused()
        {
            var current = stage->GetFocus();
            return manager->FocusedAddon == addon && (current == focus || WithinInput(current, container)
                || current == null && addon->FocusNode == focus);
        }
        if (Focused()) return true;
        keyboard.Release();
        if (now < nextFocusAttempt) return false;
        nextFocusAttempt = now + 100;
        addon->Focus();
        addon->SetComponentFocusNode(component);
        addon->SetFocusNode(focus);
        WriteRecord(new { Kind = "InputFocusRestore", At = DateTimeOffset.Now, Addon = "KTGInput",
            NodeId = focus->NodeId, ComponentType = component->GetComponentType().ToString() });
        return Focused();
    }
}
