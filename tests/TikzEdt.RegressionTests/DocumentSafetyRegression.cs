using System.IO;
using System.Reflection;
using System.Windows;
using ICSharpCode.AvalonEdit;
using TikzEdt.Modern;
using TikzEdt.Modern.Core.Documents;

internal static class DocumentSafetyRegression
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        var root = Path.GetFullPath("artifacts/document-safety-regression/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var store = new DocumentSafetyStore(Path.Combine(root, "safety")); var file = Path.Combine(root, "figure with spaces.tikz");
        var first = store.Save(file, "α العربية\r\ninitial", null);
        Require(File.ReadAllText(file).Contains("العربية") && DocumentSafetyStore.Read(file).Fingerprint == first, "UTF-8/CRLF atomic save failed.");
        store.Save(file, "second", first); Require(store.History(file).Any(c => c.Text.Contains("initial")), "Old version not preserved.");
        var expected = DocumentSafetyStore.Fingerprint(file); File.WriteAllText(file, "external change");
        try { store.Save(file, "my edit", expected); throw new Exception("External edit overwritten without consent."); } catch (DocumentConflictException) { }
        Require(File.ReadAllText(file) == "external change", "Conflict changed disk content.");
        store.Save(file, "my edit", expected, true); Require(store.History(file).Any(c => c.Text == "external change"), "Confirmed overwrite did not preserve external version.");
        var session = Guid.NewGuid(); store.WriteRecovery(session, null, "unsaved untitled");
        Require(new DocumentSafetyStore(Path.Combine(root, "safety")).Recoveries().Single().Text == "unsaved untitled", "Crash recovery did not survive a new store instance.");
        Require(!store.WriteRecovery(session, file, new string('x', DocumentSafetyStore.MaxCopyBytes + 1)) && store.Recoveries().Single().Text == "unsaved untitled", "Oversized draft removed a previous recovery.");
        var recovery = Path.Combine(root, "safety", "recovery"); File.WriteAllText(Path.Combine(recovery, Guid.NewGuid().ToString("N") + ".json"), "broken JSON");
        Require(store.Recoveries().Count == 1, "Corrupt recovery prevented reading good copies."); store.RemoveRecovery(session); Require(store.Recoveries().Count == 0, "Session recovery cleanup failed.");
        for (var i = 0; i < 60; i++) store.KeepCopy(file, "history " + i, "test");
        Require(store.History(file).Count == 50 && File.ReadAllText(file) == "my edit", "History retention modified source or exceeded limit.");
        Require(!Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "Atomic-write temporary files left behind.");
        var blockedRoot = Path.Combine(root, "blocked"); File.WriteAllText(blockedRoot, "not a directory");
        try { new DocumentSafetyStore(blockedRoot).Save(file, "must not replace", DocumentSafetyStore.Fingerprint(file)); throw new Exception("Save accepted failed safety storage."); } catch (IOException) { }
        Require(File.ReadAllText(file) == "my edit", "Backup failure overwrote the user's file.");
        Exception? error = null; var thread = new Thread(() => { try { Ui(store, file); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (error is not null) throw error;
        Console.WriteLine("A2: atomic Unicode saves, persisted recovery/history, conflict protection, overwrite backups, retention, corruption, size limits, failure safety and editor open/save/undo passed.");
    }
    static void Ui(DocumentSafetyStore store, string file)
    {
        var app = new App(); app.InitializeComponent(); var window = new MainWindow();
        typeof(MainWindow).GetField("_safety", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, store);
        window.OpenTrackedDocument(file); var editor = (TextEditor)window.FindName("Editor"); var old = editor.Text;
        editor.Document.Replace(0, editor.Text.Length, "changed in editor");
        window.SaveTrackedDocument(file, DocumentSafetyStore.Fingerprint(file));
        Require(File.ReadAllText(file) == editor.Text && store.History(file).Any(c => c.Text == old), "UI saving bypassed local history.");
        editor.Document.Replace(0, editor.Text.Length, old); editor.Undo(); Require(editor.Text == "changed in editor", "History replacement cannot be undone.");
        var dialog = new DocumentCopiesWindow("History", store.History(file)); var root = (FrameworkElement)dialog.Content;
        root.Measure(new Size(850, 600)); root.Arrange(new Rect(0, 0, 850, 600)); root.UpdateLayout(); dialog.Close(); window.Close(); app.Shutdown();
    }
}
