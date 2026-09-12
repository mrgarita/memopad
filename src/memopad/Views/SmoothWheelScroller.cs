using System.Diagnostics;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace Memopad.Views;

/// <summary>
/// 本文（RichEdit）をマウス ホイールでなめらかにスクロールする。RichEdit 自身のホイール処理の代わりに使う。
///
/// Windows 標準の RichEdit（msftedit.dll）は、本文の末尾が改行のとき、ホイールで送ると最後の空行の
/// 1 行手前で止まってしまう（FB-17。スクロールバーや PageDown では末尾まで届く）。メモ帳はアプリに同梱した
/// 新しい RichEdit を使っているので、この癖がない。そこでホイールだけは自前で受け、スクロール位置
/// （EM_SETSCROLLPOS）を少しずつ動かして、スクロールバーで一番下まで送ったときと同じ位置まで届かせる。
/// 1 ノッチで動く量は RichEdit と同じく「マウスの設定の行数」（既定 3 行）。
/// </summary>
internal sealed class SmoothWheelScroller : IDisposable
{
    private const int WheelDelta = 120;
    private const int SB_VERT = 1;
    private const int SIF_RANGE = 0x1;
    private const int SIF_PAGE = 0x2;
    private const int EM_LINEINDEX = 0x00BB;
    private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
    private const int EM_POSFROMCHAR = 0x00D6;
    private const int EM_GETSCROLLPOS = 0x04DD;
    private const int EM_SETSCROLLPOS = 0x04DE;

    /// <summary>
    /// 目標までの残りが 1/e になる時間（ms）。下の最低の速さと合わせて、1 ノッチ（3 行）はおよそ 0.25 秒で止まる
    /// （RichEdit 自身のなめらかスクロールは同じ距離を約 0.3 秒かけて等速で動かす）。
    /// </summary>
    private const double TimeConstantMs = 70;

    /// <summary>止まり際の最低の速さ（96 dpi での px/ms）。これが無いと最後の数 px を 1 px ずつ這うように動く。</summary>
    private const double MinSpeed = 0.06;

    /// <summary>1 コマの間隔の目安（ms）。回し始めの一歩をすぐに動かすときに使う。</summary>
    private const double FrameMs = 16;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, ref POINT lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, int msg, ref POINT wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetScrollInfo(IntPtr hWnd, int bar, ref SCROLLINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SCROLLINFO { public int cbSize, fMask, nMin, nMax, nPage, nPos, nTrackPos; }

    private readonly WinForms.Control _edit;
    private readonly WinForms.Timer _timer = new() { Interval = 10 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _target;     // 目標の位置（文書の先頭からの px）
    private double _current;    // 動かしている途中の位置（小数まで持つ）
    private int _lastSet;       // 最後に設定した位置。これとずれていたら、ほかの操作でスクロールされた
    private double _lastTick;

    public SmoothWheelScroller(WinForms.Control edit)
    {
        _edit = edit;
        _timer.Tick += (_, _) => Step();
    }

    /// <summary>ホイールを回した。delta は WM_MOUSEWHEEL の値（手前に回すと負）。</summary>
    public void Scroll(int delta)
    {
        var h = _edit.Handle;
        if (!TryGetBottom(h, out var bottom, out var page)) return;   // 本文が画面に収まっている
        var pos = GetScrollPos(h);
        // キー操作などでほかからスクロールされていたら、今の位置から数え直す。
        // そうでなければ小数の端数を持ち越す（タッチパッドの細かい回転を整数に丸めて量が膨らまないように）
        if (pos.Y != _lastSet)
        {
            _current = _target = _lastSet = pos.Y;
        }
        // タッチパッドなどの細かい回転（120 未満）も、その割合だけ動かす
        _target = Math.Clamp(_target - delta * PixelsPerNotch(h, page) / WheelDelta, 0, bottom);
        if (!_timer.Enabled)
        {
            // 最初の一歩はすぐに動かし、ホイールへの反応を待たせない
            _lastTick = _clock.Elapsed.TotalMilliseconds - FrameMs;
            _timer.Start();
            Step();
        }
    }

    public void Dispose() => _timer.Dispose();

    private void Step()
    {
        if (!_edit.IsHandleCreated) { _timer.Stop(); return; }
        var h = _edit.Handle;
        var pos = GetScrollPos(h);
        // キー操作・スクロールバー・入力などでほかからスクロールされたら、そちらを優先して止める
        if (pos.Y != _lastSet) { _timer.Stop(); return; }
        // 本文が画面に収まった（行の削除など）ときは、その場で止める
        if (!TryGetBottom(h, out var bottom, out _))
        {
            _current = _target = pos.Y;
            _timer.Stop();
            return;
        }
        var now = _clock.Elapsed.TotalMilliseconds;
        var elapsed = now - _lastTick;
        _lastTick = now;
        // 残りの距離に比例して進める（動き始めが速く、止まり際がゆるやかになる）
        _target = Math.Min(_target, bottom);
        var remaining = _target - _current;
        var step = remaining * (1 - Math.Exp(-elapsed / TimeConstantMs));
        var minStep = MinSpeed * _edit.DeviceDpi / 96 * elapsed;
        if (Math.Abs(step) < minStep) step = Math.Sign(remaining) * Math.Min(minStep, Math.Abs(remaining));
        _current += step;
        if (Math.Abs(_target - _current) < 0.5)
        {
            _current = _target;
            _timer.Stop();
        }
        var y = (int)Math.Round(_current);
        if (y != pos.Y) SetScrollPos(h, pos.X, y);
        _lastSet = y;
    }

    /// <summary>1 ノッチで動かす量（px）。</summary>
    private double PixelsPerNotch(IntPtr h, int page)
    {
        var lines = WinForms.SystemInformation.MouseWheelScrollLines;
        // マウスの設定が「1 画面ずつスクロール」（-1）なら、1 ノッチで 1 画面分
        return lines < 0 ? page : lines * LineHeight(h);
    }

    /// <summary>画面の一番上に見えている行の高さ（px。ズームの倍率を含む）。</summary>
    private int LineHeight(IntPtr h)
    {
        var first = (int)SendMessageW(h, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
        // 一番上が最後の行なら、ひとつ前の行で測る
        for (var line = first; line >= Math.Max(0, first - 1); line--)
        {
            var a = (int)SendMessageW(h, EM_LINEINDEX, line, IntPtr.Zero);
            var b = (int)SendMessageW(h, EM_LINEINDEX, line + 1, IntPtr.Zero);
            if (a < 0 || b < 0) continue;
            var pa = new POINT();
            var pb = new POINT();
            SendMessageW(h, EM_POSFROMCHAR, ref pa, a);
            SendMessageW(h, EM_POSFROMCHAR, ref pb, b);
            if (pb.Y > pa.Y) return pb.Y - pa.Y;
        }
        return _edit.Font.Height;
    }

    /// <summary>
    /// 一番下まで送ったときの位置（px）。RichEdit は nMax に文書の高さそのものを入れるので、
    /// スクロールバーで一番下（SB_BOTTOM）へ送ったときの位置は nMax - nPage になる。
    /// </summary>
    private static bool TryGetBottom(IntPtr h, out int bottom, out int page)
    {
        var si = new SCROLLINFO { cbSize = Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_RANGE | SIF_PAGE };
        if (!GetScrollInfo(h, SB_VERT, ref si) || si.nPage <= 0)
        {
            bottom = page = 0;
            return false;
        }
        page = si.nPage;
        bottom = Math.Max(0, si.nMax - si.nPage);
        return bottom > 0;
    }

    // EM_GETSCROLLPOS / EM_SETSCROLLPOS は 32 bit の位置を扱える（12 万行・約 444 万 px で確認）。
    // EM_SETSCROLLPOS は末尾を越えた位置も受け付けるので、呼ぶ側で一番下までに抑える

    private static POINT GetScrollPos(IntPtr h)
    {
        var p = new POINT();
        SendMessageW(h, EM_GETSCROLLPOS, IntPtr.Zero, ref p);
        return p;
    }

    private static void SetScrollPos(IntPtr h, int x, int y)
    {
        var p = new POINT { X = x, Y = y };
        SendMessageW(h, EM_SETSCROLLPOS, IntPtr.Zero, ref p);
    }
}
