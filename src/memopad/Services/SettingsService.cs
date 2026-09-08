using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memopad.Services;

/// <summary>
/// 設定ファイルの読み書き。壊れていても起動できるよう、失敗時は既定値に戻す。
/// JSON の変換コードはソース ジェネレーターで生成する（リフレクションで組み立てると起動時に約 30 ms かかっていた）。
/// </summary>
public static class SettingsService
{
    private static readonly SettingsJsonContext Context = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    });

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "memopad");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize(json, Context.AppSettings) ?? new AppSettings();
            }
        }
        catch
        {
            // 設定が読めなくても起動は続ける
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Context.AppSettings));
        }
        catch
        {
            // 保存に失敗しても終了処理は止めない
        }
    }
}

/// <summary>AppSettings の JSON 変換コード（コンパイル時に生成される）。</summary>
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
