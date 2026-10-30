using System.Text.Json;

namespace MakoMaster;

internal enum AutoEntryAction { None, Start, Stop }

internal sealed class AutoEntrySession
{
    private bool blocked;
    internal bool OwnsRun { get; private set; }
    internal void SuppressCurrentGame() { blocked = true; OwnsRun = false; }
    internal AutoEntryAction Tick(bool enabled, bool game, bool ended, bool busy, bool canRearm = false)
    {
        if (ended)
        {
            blocked = !canRearm;
            if (!OwnsRun) return AutoEntryAction.None;
            OwnsRun = false;
            return AutoEntryAction.Stop;
        }
        if (!enabled && OwnsRun)
        {
            SuppressCurrentGame();
            return AutoEntryAction.Stop;
        }
        if (!enabled || !game || blocked || busy || OwnsRun) return AutoEntryAction.None;
        blocked = true; OwnsRun = true;
        return AutoEntryAction.Start;
    }
}

internal sealed record AutoEntrySettings(bool Enabled = true)
{
    internal static AutoEntrySettings Load(string file) => File.Exists(file)
        ? JsonSerializer.Deserialize<AutoEntrySettings>(File.ReadAllText(file)) ?? new() : new();
    internal void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(this));
        File.Move(file + ".tmp", file, true);
    }
}
