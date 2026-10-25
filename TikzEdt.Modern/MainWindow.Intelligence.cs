using TikzEdt.Modern.Core.Documents;
using System.IO;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private TexIndex? _texIndex;
    private CancellationTokenSource? _indexJob;
    private Task<TexIndex?>? _indexTask;
    private string? _indexText, _indexPreamble;
    private string ReadIntelligencePreamble()
    {
        try { return PreambleSettings.ReadSnapshot().Content ?? ""; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }
    private Task<TexIndex?> RefreshTexIndexAsync(string source)
    {
        var preamble = ReadIntelligencePreamble();
        if (_indexText == source && _indexPreamble == preamble && _indexTask is not null && _indexJob?.IsCancellationRequested == false) return _indexTask;
        _indexJob?.Cancel(); _indexJob?.Dispose(); var job = _indexJob = new CancellationTokenSource(); var token = job.Token;
        _indexText = source; _indexPreamble = preamble;
        return _indexTask = BuildAsync();
        async Task<TexIndex?> BuildAsync()
        {
        try
        {
            var index = await Task.Run(() => TexKnowledge.Build(source, preamble, token), token);
            if (!token.IsCancellationRequested && Editor.Text == source) _texIndex = index;
            return token.IsCancellationRequested ? null : index;
        }
        catch (OperationCanceledException) { return null; }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return null; }
        }
    }
}
