using System.Windows;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Folding;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private FoldingManager? _foldingManager;
    private readonly DispatcherTimer _foldingTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    private void InitializeFolding()
    {
        _foldingManager = FoldingManager.Install(Editor.TextArea);
        _foldingTimer.Tick += (_, _) => { _foldingTimer.Stop(); RefreshFoldings(); };
        Editor.TextChanged += (_, _) => { _foldingTimer.Stop(); _foldingTimer.Start(); };
        Closed += (_, _) => { _foldingTimer.Stop(); if (_foldingManager is not null) FoldingManager.Uninstall(_foldingManager); };
    }

    private void RefreshFoldings() => _foldingManager?.UpdateFoldings(TikzFolding.Create(Editor.Text), -1);
    private void FoldAll_Click(object sender, RoutedEventArgs e)
    {
        RefreshFoldings();
        if (_foldingManager is not null) foreach (var folding in _foldingManager.AllFoldings) folding.IsFolded = true;
    }
    private void UnfoldAll_Click(object sender, RoutedEventArgs e)
    {
        if (_foldingManager is not null) foreach (var folding in _foldingManager.AllFoldings) folding.IsFolded = false;
    }
}
