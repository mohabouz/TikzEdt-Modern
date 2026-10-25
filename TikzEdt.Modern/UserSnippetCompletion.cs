using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using TikzEdt.Modern.Core.Documents;

namespace TikzEdt.Modern;

internal sealed class UserSnippetCompletion(UserSnippet snippet, Action<ISegment> insert) : ICompletionData
{
    public ImageSource? Image => null;
    public string Text => snippet.Prefix;
    public object Content => "S  " + snippet.Prefix + " · " + snippet.Name;
    public object Description => "User snippet · " + snippet.Description + "\nTab/Shift+Tab visits linked fields.\n" + snippet.Body;
    public double Priority => 115;
    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) => insert(completionSegment);
}
