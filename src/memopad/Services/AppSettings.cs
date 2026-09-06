namespace Memopad.Services;

/// <summary>
/// 永続化する設定。%APPDATA%\memopad\settings.json に JSON で保存する。
/// 色は "#RRGGBB" の文字列で持ち、空文字ならテーマ既定の色を使う。
/// </summary>
public sealed class AppSettings
{
    // --- フォント
    public string FontFamily { get; set; } = "Consolas";
    public double FontSize { get; set; } = 11;
    public bool FontBold { get; set; }
    public bool FontItalic { get; set; }

    // --- 表示
    public bool WordWrap { get; set; } = true;
    public bool ShowStatusBar { get; set; } = true;

    /// <summary>"Light" / "Dark" / "System"</summary>
    public string Theme { get; set; } = "System";

    // --- memopad の追加機能：背景色・文字色
    public string BackgroundColor { get; set; } = "";
    public string ForegroundColor { get; set; } = "";

    // --- 最近使ったファイル
    public bool RecentFilesEnabled { get; set; } = true;
    public List<string> RecentFiles { get; set; } = new();

    // --- ウィンドウ
    public double WindowWidth { get; set; } = 1000;
    public double WindowHeight { get; set; } = 700;
    public bool WindowMaximized { get; set; }

    // --- ページ設定（余白は mm）
    public bool PrintLandscape { get; set; }
    public double MarginLeft { get; set; } = 20;
    public double MarginRight { get; set; } = 20;
    public double MarginTop { get; set; } = 25;
    public double MarginBottom { get; set; } = 25;
    public string PrintHeader { get; set; } = "&f";
    public string PrintFooter { get; set; } = "ページ &p";

    public const int MaxRecentFiles = 10;

    /// <summary>最近使ったファイルの先頭に追加する（重複は前へ移動、上限を超えた分は捨てる）。</summary>
    public void PushRecentFile(string path)
    {
        if (!RecentFilesEnabled) return;
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecentFiles) RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
    }
}
