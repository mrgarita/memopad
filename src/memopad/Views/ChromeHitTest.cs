using System.Drawing;
using System.Windows.Forms;

namespace Memopad.Views;

/// <summary>
/// ウィンドウの枠（タイトル行のドラッグ移動とリサイズ）の当たり判定を、子コントロールから
/// 親フォームへ通すための仕掛け。
///
/// 標準のタイトル バーを外したウィンドウ（<c>WM_NCCALCSIZE</c>）では、どこがタイトル行で
/// どこが枠かを <see cref="MainForm"/> が <c>WM_NCHITTEST</c> で答える。ところが Windows は
/// その座標にある一番手前の子ウィンドウに判定を尋ねるため、タイトル行やウィンドウの縁を
/// 子コントロールが覆っていると親の答えが使われない。v0.8.0 でメイン ウィンドウを
/// Windows Forms（＝コントロールごとに HWND がある）に作り直したときに、これが原因で
/// タイトル行のドラッグ移動・ダブルクリックでの最大化・縁のリサイズができなくなっていた。
///
/// 子側で <c>HTTRANSPARENT</c> を返すと、その座標の判定は下にある親ウィンドウへ落ちる。
/// これでドラッグ移動も最大化もリサイズも Windows が面倒を見てくれる状態に戻る。
/// </summary>
internal static class ChromeHitTest
{
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;

    /// <summary>
    /// <c>WM_NCHITTEST</c> を親フォームに任せたら true（呼び出し側は base.WndProc を呼ばない）。
    /// タイトル行の空きとウィンドウの縁だけが対象で、それ以外は今までどおり子コントロールが受け取る。
    /// </summary>
    public static bool TryPassToFrame(Control control, ref Message m)
    {
        if (m.Msg != WM_NCHITTEST) return false;
        if (control.FindForm() is not MainForm form) return false;
        var lp = (int)(long)m.LParam;
        var screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
        if (!form.IsWindowFrameAt(form.PointToClient(screen))) return false;
        m.Result = HTTRANSPARENT;
        return true;
    }
}

/// <summary>枠の当たり判定を親へ通す <see cref="Panel"/>（本文の入れ物と検索バーに使う）。</summary>
internal sealed class ChromePanel : Panel
{
    protected override void WndProc(ref Message m)
    {
        if (ChromeHitTest.TryPassToFrame(this, ref m)) return;
        base.WndProc(ref m);
    }
}

/// <summary>枠の当たり判定を親へ通す <see cref="MenuStrip"/>（左右の端がリサイズの縁に重なる）。</summary>
internal sealed class ChromeMenuStrip : MenuStrip
{
    protected override void WndProc(ref Message m)
    {
        if (ChromeHitTest.TryPassToFrame(this, ref m)) return;
        base.WndProc(ref m);
    }
}
