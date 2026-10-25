using System.IO;
using System.Windows;
using System.Windows.Threading;
using TikzEdt.Modern.Core.Documents;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private readonly DocumentSafetyStore _safety = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TikzEdt", "Modern", "DocumentSafety"));
    private readonly Guid _recoverySession = Guid.NewGuid();
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Task _recoveryWrite = Task.CompletedTask;
    private string? _diskFingerprint;
    private string? _draftText;
    private string? _externalWarning;
    private int _documentEpoch;
    private bool _safetyClosed;
    private Guid? _restoredRecovery;
    private void InitializeDocumentSafety()
    {
        _recoveryTimer.Tick += async (_, _) =>
        {
            if (!_recoveryWrite.IsCompleted) return;
            var text = Editor.Text; var path = _filePath; var epoch = _documentEpoch;
            try
            {
                if (_isDirty && _draftText != text)
                {
                    var written = false;
                    _recoveryWrite = Task.Run(() => written = _safety.WriteRecovery(_recoverySession, path, text)); await _recoveryWrite;
                    if (_safetyClosed || epoch != _documentEpoch) return;
                    if (written) _draftText = text; else ModeStatus.Text = "Recovery limit is 2 MB. Save this large document frequently.";
                }
                if (path is not null)
                {
                    var actual = await Task.Run(() => DocumentSafetyStore.Fingerprint(path));
                    if (!_safetyClosed && epoch == _documentEpoch && path == _filePath && actual != _diskFingerprint && _externalWarning != (actual ?? "deleted"))
                    { _externalWarning = actual ?? "deleted"; ModeStatus.Text = "File changed outside TikzEdt. Use File → Reload from disk, or save after reviewing the conflict."; }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ModeStatus.Text = "Document safety: " + e.Message; }
        };
        Loaded += (_, _) =>
        {
            _recoveryTimer.Start();
            try { if (_safety.Recoveries().Count > 0) ModeStatus.Text = "Unsaved recovery copies are available: File → Recover unsaved documents."; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ModeStatus.Text = "Cannot read recovery copies: " + e.Message; }
        };
        Closing += (_, e) => { if (IsLoaded && !ConfirmDiscardChanges()) e.Cancel = true; };
        Closed += (_, _) =>
        {
            _safetyClosed = true; _recoveryTimer.Stop();
            try { _recoveryWrite.GetAwaiter().GetResult(); _safety.RemoveRecovery(_recoverySession); if (_restoredRecovery is { } restored) _safety.RemoveRecovery(restored); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Keep the copy rather than risk losing it. */ }
        };
    }
    private void TrackOpenedDocument()
    {
        FinishUserSnippet();
        _documentEpoch++;
        try { _recoveryWrite.GetAwaiter().GetResult(); _safety.RemoveRecovery(_recoverySession); if (_restoredRecovery is { } restored) _safety.RemoveRecovery(restored); _restoredRecovery = null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ModeStatus.Text = "Cannot clear the old recovery copy: " + e.Message; }
        _diskFingerprint = null;
        _draftText = null; _externalWarning = null;
    }
    internal void OpenTrackedDocument(string path)
    {
        var document = DocumentSafetyStore.Read(path);
        _filePath = Path.GetFullPath(path); TrackOpenedDocument(); _diskFingerprint = document.Fingerprint;
        Editor.Text = document.Text; _isDirty = false; UpdateTitle();
    }
    internal void SaveTrackedDocument(string path, string? expectedFingerprint, bool overwrite = false)
    {
        var fingerprint = _safety.Save(path, Editor.Text, expectedFingerprint, overwrite);
        _filePath = Path.GetFullPath(path); TrackOpenedDocument(); _diskFingerprint = fingerprint;
        _isDirty = false; UpdateTitle();
        ModeStatus.Text = $"Saved {_filePath}" + (System.Text.Encoding.UTF8.GetByteCount(Editor.Text) > DocumentSafetyStore.MaxCopyBytes ? " · above 2 MB history/recovery limit" : " · local history checkpoint kept");
    }
    private void RecoverDocuments_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DocumentCopiesWindow("Recover unsaved documents", _safety.Recoveries().Where(c => c.Id != _recoverySession.ToString("N")).ToArray()) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Selected is not { } copy || !ConfirmDiscardChanges()) return;
        var fingerprint = copy.Path is null ? null : DocumentSafetyStore.Fingerprint(copy.Path);
        _filePath = copy.Path; TrackOpenedDocument(); _diskFingerprint = fingerprint; Editor.Text = copy.Text; _isDirty = true; UpdateTitle();
        _restoredRecovery = Guid.ParseExact(copy.Id, "N");
        _safety.WriteRecovery(_recoverySession, _filePath, Editor.Text); _draftText = Editor.Text;
        // Keep the original recovery until the user explicitly saves/discards the restored document.
        ModeStatus.Text = "Recovered into the editor. Review and save; the original recovery copy is retained.";
    }
    private void VersionHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_filePath is null) { ModeStatus.Text = "Save the document first to create version history."; return; }
        var dialog = new DocumentCopiesWindow("Local version history", _safety.History(_filePath)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Selected is not { } copy) return;
        _safety.KeepCopy(_filePath, Editor.Text, "before-restore");
        Editor.Document.Replace(0, Editor.Document.TextLength, copy.Text); // One undoable edit; never overwrite disk here.
        ModeStatus.Text = "History restored into editor · Ctrl+Z to undo · save when ready.";
    }
    private void ReloadDocument_Click(object sender, RoutedEventArgs e)
    {
        if (_filePath is null) return;
        if (_isDirty && MessageBox.Show(this, "Keep a history copy and reload from disk?", "Reload document", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try
        {
            _safety.KeepCopy(_filePath, Editor.Text, "before-reload");
            var disk = DocumentSafetyStore.Read(_filePath); Editor.Document.Replace(0, Editor.Document.TextLength, disk.Text); _isDirty = false;
            TrackOpenedDocument(); _diskFingerprint = disk.Fingerprint; UpdateTitle();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ModeStatus.Text = error.Message; }
    }
}
