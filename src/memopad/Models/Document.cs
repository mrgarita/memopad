using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Memopad.Services;

namespace Memopad.Models;

/// <summary>
/// タブ 1 つ分の文書。本文そのものはエディタ（TextBox）が持ち、ここではファイルの属性と変更状態を持つ。
/// </summary>
public sealed class Document : INotifyPropertyChanged
{
    private static int _untitledCounter;

    private string? _filePath;
    private bool _isDirty;
    private TextEncodingKind _encoding = TextEncodingKind.Utf8;
    private LineEndingKind _lineEnding = LineEndingKind.Crlf;

    public Document()
    {
        // 「タイトルなし」「タイトルなし 2」… とメモ帳風に採番する
        var n = Interlocked.Increment(ref _untitledCounter);
        UntitledName = n == 1 ? "タイトルなし" : $"タイトルなし {n}";
    }

    public string UntitledName { get; }

    /// <summary>保存先。未保存の新規文書なら null。</summary>
    public string? FilePath
    {
        get => _filePath;
        set { if (Set(ref _filePath, value)) { Raise(nameof(Title)); Raise(nameof(TabTitle)); } }
    }

    /// <summary>未保存の変更があるか。タブの●印とタイトルバーの * に使う。</summary>
    public bool IsDirty
    {
        get => _isDirty;
        set { if (Set(ref _isDirty, value)) Raise(nameof(TabTitle)); }
    }

    public TextEncodingKind Encoding
    {
        get => _encoding;
        set => Set(ref _encoding, value);
    }

    public LineEndingKind LineEnding
    {
        get => _lineEnding;
        set => Set(ref _lineEnding, value);
    }

    /// <summary>タブとタイトルバーに出す名前（拡張子付きのファイル名、または「タイトルなし」）。</summary>
    public string Title => FilePath is null ? UntitledName : Path.GetFileName(FilePath);

    /// <summary>タブに出す名前。未保存なら●を付ける。</summary>
    public string TabTitle => IsDirty ? $"● {Title}" : Title;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
