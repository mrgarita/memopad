using System.Reflection;
using System.Windows;
using Memopad.Services;

namespace Memopad.Dialogs;

/// <summary>「MemoPad について」。バージョンと設定ファイルの場所を表示する。</summary>
public partial class AboutDialog : Window
{
    public AboutDialog(IntPtr owner)
    {
        InitializeComponent();
        new System.Windows.Interop.WindowInteropHelper(this).Owner = owner;
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"バージョン {version?.ToString(3) ?? "?"}";
        SettingsPathText.Text = SettingsService.SettingsPath;
    }
}
