using System.Windows.Input;

namespace Memopad;

/// <summary>
/// メニューとショートカットで使うコマンド。キー割り当てはメモ帳（Store 版 11.x）に合わせる。
/// 切り取り・コピー・貼り付け・元に戻す・やり直し・すべて選択は RichEdit が自前で処理するので、
/// メニュー用に Click ハンドラで呼ぶ（コマンドは持たない）。
/// </summary>
public static class Commands
{
    private static RoutedUICommand Make(string text, string name, params InputGesture[] gestures)
    {
        var cmd = new RoutedUICommand(text, name, typeof(Commands));
        foreach (var g in gestures) cmd.InputGestures.Add(g);
        return cmd;
    }

    // ファイル
    public static readonly RoutedUICommand NewTab = Make("新しいタブ", nameof(NewTab), new KeyGesture(Key.N, ModifierKeys.Control));
    public static readonly RoutedUICommand NewWindow = Make("新しいウィンドウ", nameof(NewWindow), new KeyGesture(Key.N, ModifierKeys.Control | ModifierKeys.Shift));
    public static readonly RoutedUICommand Open = Make("開く", nameof(Open), new KeyGesture(Key.O, ModifierKeys.Control));
    public static readonly RoutedUICommand Save = Make("保存", nameof(Save), new KeyGesture(Key.S, ModifierKeys.Control));
    public static readonly RoutedUICommand SaveAs = Make("名前を付けて保存", nameof(SaveAs), new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift));
    public static readonly RoutedUICommand SaveAll = Make("すべて保存", nameof(SaveAll), new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Alt));
    public static readonly RoutedUICommand PageSetup = Make("ページ設定", nameof(PageSetup));
    public static readonly RoutedUICommand Print = Make("印刷", nameof(Print), new KeyGesture(Key.P, ModifierKeys.Control));
    public static readonly RoutedUICommand CloseTab = Make("タブを閉じる", nameof(CloseTab), new KeyGesture(Key.W, ModifierKeys.Control));
    public static readonly RoutedUICommand CloseWindow = Make("ウィンドウを閉じる", nameof(CloseWindow), new KeyGesture(Key.W, ModifierKeys.Control | ModifierKeys.Shift));
    public static readonly RoutedUICommand Exit = Make("終了", nameof(Exit));

    // 編集
    public static readonly RoutedUICommand Find = Make("検索", nameof(Find), new KeyGesture(Key.F, ModifierKeys.Control));
    public static readonly RoutedUICommand FindNext = Make("次を検索", nameof(FindNext), new KeyGesture(Key.F3));
    public static readonly RoutedUICommand FindPrevious = Make("前を検索", nameof(FindPrevious), new KeyGesture(Key.F3, ModifierKeys.Shift));
    public static readonly RoutedUICommand Replace = Make("置換", nameof(Replace), new KeyGesture(Key.H, ModifierKeys.Control));
    public static readonly RoutedUICommand GoTo = Make("行へ移動", nameof(GoTo), new KeyGesture(Key.G, ModifierKeys.Control));
    public static readonly RoutedUICommand InsertDateTime = Make("日付と時刻", nameof(InsertDateTime), new KeyGesture(Key.F5));

    // 表示
    public static readonly RoutedUICommand ZoomIn = Make("拡大", nameof(ZoomIn),
        new KeyGesture(Key.OemPlus, ModifierKeys.Control), new KeyGesture(Key.Add, ModifierKeys.Control));
    public static readonly RoutedUICommand ZoomOut = Make("縮小", nameof(ZoomOut),
        new KeyGesture(Key.OemMinus, ModifierKeys.Control), new KeyGesture(Key.Subtract, ModifierKeys.Control));
    public static readonly RoutedUICommand ZoomReset = Make("既定の倍率に戻す", nameof(ZoomReset),
        new KeyGesture(Key.D0, ModifierKeys.Control), new KeyGesture(Key.NumPad0, ModifierKeys.Control));
    public static readonly RoutedUICommand ToggleStatusBar = Make("ステータス バー", nameof(ToggleStatusBar));
    public static readonly RoutedUICommand ToggleWordWrap = Make("右端で折り返す", nameof(ToggleWordWrap));

    // 書式（memopad の追加機能）
    public static readonly RoutedUICommand Font = Make("フォント...", nameof(Font));
    public static readonly RoutedUICommand ResetColors = Make("既定の色に戻す", nameof(ResetColors));

    // ヘルプ
    public static readonly RoutedUICommand About = Make("memopad について", nameof(About));

    /// <summary>ショートカットを持つコマンドの一覧。エディタ（RichEdit）で押されたキーをコマンドに変換するときに使う。</summary>
    public static readonly RoutedUICommand[] All =
    {
        NewTab, NewWindow, Open, Save, SaveAs, SaveAll, PageSetup, Print, CloseTab, CloseWindow, Exit,
        Find, FindNext, FindPrevious, Replace, GoTo, InsertDateTime,
        ZoomIn, ZoomOut, ZoomReset, ToggleStatusBar, ToggleWordWrap,
        Font, ResetColors, About,
    };
}
