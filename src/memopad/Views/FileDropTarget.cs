using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace Memopad.Views;

/// <summary>OLE のドロップ先（<c>IDropTarget</c>）。ウィンドウ 1 つにつき 1 つだけ登録できる。</summary>
[ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleDropTarget
{
    // POINTL（LONG 2 つ）は値渡しなので long 1 つとして受け取る
    [PreserveSig] int OleDragEnter([In, MarshalAs(UnmanagedType.Interface)] object pDataObj, [In] int grfKeyState, [In] long pt, [In, Out] ref int pdwEffect);
    [PreserveSig] int OleDragOver([In] int grfKeyState, [In] long pt, [In, Out] ref int pdwEffect);
    [PreserveSig] int OleDragLeave();
    [PreserveSig] int OleDrop([In, MarshalAs(UnmanagedType.Interface)] object pDataObj, [In] int grfKeyState, [In] long pt, [In, Out] ref int pdwEffect);
}

/// <summary>
/// ファイルのドロップだけを受け取るドロップ先。本文（RichEdit）に登録して使う。
///
/// RichEdit は自分でもドロップを処理する。Windows Forms 経由でドロップのイベントを受け取って
/// ファイルを開いても、そのあと RichEdit 側の処理が走って変更通知（EN_CHANGE）が上がるため、
/// 開いたばかりの文書が「編集済み」（タイトルの * とタブの●）になってしまっていた（v0.8.0 の不具合）。
/// ドロップ先を丸ごとこのクラスに差し替え、RichEdit にドロップを渡さないことで、
/// 「ファイルを開くだけ」の動作にする。
/// </summary>
internal sealed class FileDropTarget : IOleDropTarget
{
    private const int S_OK = 0;
    private const int DropEffectNone = 0;
    private const int DropEffectCopy = 1;

    private readonly Action<string[]> _onFiles;
    private bool _acceptable;

    public FileDropTarget(Action<string[]> onFiles) => _onFiles = onFiles;

    int IOleDropTarget.OleDragEnter(object pDataObj, int grfKeyState, long pt, ref int pdwEffect)
    {
        _acceptable = GetFiles(pDataObj) is { Length: > 0 };
        pdwEffect = _acceptable ? DropEffectCopy : DropEffectNone;
        return S_OK;
    }

    int IOleDropTarget.OleDragOver(int grfKeyState, long pt, ref int pdwEffect)
    {
        pdwEffect = _acceptable ? DropEffectCopy : DropEffectNone;
        return S_OK;
    }

    int IOleDropTarget.OleDragLeave()
    {
        _acceptable = false;
        return S_OK;
    }

    int IOleDropTarget.OleDrop(object pDataObj, int grfKeyState, long pt, ref int pdwEffect)
    {
        _acceptable = false;
        var files = GetFiles(pDataObj);
        pdwEffect = files is { Length: > 0 } ? DropEffectCopy : DropEffectNone;
        if (files is { Length: > 0 }) _onFiles(files);
        return S_OK;
    }

    /// <summary>ドロップされたデータからファイルの一覧を取り出す。ファイルでなければ null。</summary>
    private static string[]? GetFiles(object? dataObject)
    {
        if (dataObject is not System.Runtime.InteropServices.ComTypes.IDataObject com) return null;
        try
        {
            return new WinForms.DataObject(com).GetData(WinForms.DataFormats.FileDrop) as string[];
        }
        catch
        {
            // 扱えない形式のドロップは受け付けない
            return null;
        }
    }
}
