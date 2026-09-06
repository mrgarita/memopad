using System.IO;
using System.Text;

namespace Memopad.Services;

/// <summary>ファイルのエンコード。ステータスバーの表示名と保存ダイアログの選択肢に対応する。</summary>
public enum TextEncodingKind
{
    Ansi,
    Utf16LE,
    Utf16BE,
    Utf8,
    Utf8Bom,
}

/// <summary>改行コード。ステータスバーに表示する。</summary>
public enum LineEndingKind
{
    Crlf,
    Lf,
    Cr,
}

public static class TextEncodingKindExtensions
{
    public static string DisplayName(this TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Ansi => "ANSI",
        TextEncodingKind.Utf16LE => "UTF-16 LE",
        TextEncodingKind.Utf16BE => "UTF-16 BE",
        TextEncodingKind.Utf8 => "UTF-8",
        TextEncodingKind.Utf8Bom => "UTF-8 (BOM 付き)",
        _ => kind.ToString(),
    };

    public static string DisplayName(this LineEndingKind kind) => kind switch
    {
        LineEndingKind.Crlf => "Windows (CRLF)",
        LineEndingKind.Lf => "Unix (LF)",
        LineEndingKind.Cr => "Macintosh (CR)",
        _ => kind.ToString(),
    };

    public static string NewLine(this LineEndingKind kind) => kind switch
    {
        LineEndingKind.Lf => "\n",
        LineEndingKind.Cr => "\r",
        _ => "\r\n",
    };

    public static Encoding ToEncoding(this TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Ansi => Encoding.GetEncoding(932),
        TextEncodingKind.Utf16LE => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
        TextEncodingKind.Utf16BE => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
        TextEncodingKind.Utf8Bom => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
        _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
    };
}

/// <summary>読み込んだテキストと、判定したエンコード・改行コード。</summary>
public sealed record TextFileContent(string Text, TextEncodingKind Encoding, LineEndingKind LineEnding);

/// <summary>
/// テキストファイルの読み書き。BOM でエンコードを判定し、BOM が無ければ UTF-8 として妥当か検証し、
/// 妥当でなければ ANSI（Shift_JIS）とみなす。メモ帳と同じ挙動を狙う。
/// </summary>
public static class TextFileService
{
    public static TextFileContent Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var kind = DetectEncoding(bytes);
        var encoding = kind.ToEncoding();
        var text = encoding.GetString(bytes);
        // GetString は BOM を文字として残すことがあるので取り除く
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var lineEnding = DetectLineEnding(text);
        return new TextFileContent(text, kind, lineEnding);
    }

    public static void Write(string path, string text, TextEncodingKind encoding, LineEndingKind lineEnding)
    {
        // エディタ内部は CRLF で統一しているので、保存時にファイルの改行コードへ戻す
        var normalized = NormalizeToCrlf(text).Replace("\r\n", lineEnding.NewLine());
        var enc = encoding.ToEncoding();
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var preamble = enc.GetPreamble();
        if (preamble.Length > 0) stream.Write(preamble, 0, preamble.Length);
        var body = enc.GetBytes(normalized);
        stream.Write(body, 0, body.Length);
    }

    /// <summary>LF・CR 混在のテキストを CRLF に揃える（エディタ内部表現）。</summary>
    public static string NormalizeToCrlf(string text)
    {
        if (text.IndexOf('\r') < 0 && text.IndexOf('\n') < 0) return text;
        var sb = new StringBuilder(text.Length + 16);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                sb.Append("\r\n");
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            else if (c == '\n')
            {
                sb.Append("\r\n");
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public static TextEncodingKind DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return TextEncodingKind.Utf8Bom;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return TextEncodingKind.Utf16LE;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return TextEncodingKind.Utf16BE;
        return IsValidUtf8(bytes) ? TextEncodingKind.Utf8 : TextEncodingKind.Ansi;
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        try
        {
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>最初に現れる改行で改行コードを判定する。改行が無ければ CRLF。</summary>
    public static LineEndingKind DetectLineEnding(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r') return i + 1 < text.Length && text[i + 1] == '\n' ? LineEndingKind.Crlf : LineEndingKind.Cr;
            if (text[i] == '\n') return LineEndingKind.Lf;
        }
        return LineEndingKind.Crlf;
    }
}
