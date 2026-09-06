using Memopad.Models;

namespace Memopad.Views;

/// <summary>タブ 1 つ分（文書＋その編集領域）。タブ ストリップの ItemsSource に並べる。</summary>
public sealed class DocumentTab
{
    public DocumentTab(Document document)
    {
        Document = document;
        Editor = new EditorView(document);
    }

    public Document Document { get; }
    public EditorView Editor { get; }
}
