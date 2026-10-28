using System.Text;
using System.Text.Json;

namespace MakoMaster;

internal sealed record TimingSettings(int KeyMinMs = 40, int KeyMaxMs = 100,
    int WordMinMs = 800, int WordMaxMs = 1600)
{
    internal string? ValidationError => KeyMinMs < 10 || KeyMaxMs > 2000 || KeyMinMs > KeyMaxMs
        ? "按键范围应为 10～2000 毫秒，最小值不能大于最大值。"
        : WordMinMs < 0 || WordMaxMs > 30000 || WordMinMs > WordMaxMs
            ? "每题范围应为 0～30000 毫秒，最小值不能大于最大值。" : null;
    internal int NextKey(Random random) => random.Next(KeyMinMs, KeyMaxMs + 1);
    internal int NextWord(Random random) => random.Next(WordMinMs, WordMaxMs + 1);
    internal static TimingSettings Load(string path)
    {
        var settings = File.Exists(path) ? JsonSerializer.Deserialize<TimingSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("时间设置文件无效") : new();
        if (settings.ValidationError != null) throw new InvalidDataException(settings.ValidationError);
        return settings;
    }
    internal void Save(string path)
    {
        if (ValidationError != null) throw new InvalidDataException(ValidationError);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
