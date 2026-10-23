using System.Windows;
using System.Windows.Controls;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private void Labo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var scene = LaboScene.ReadDocument(Editor.Text);
            var dialog = new LaboEditorWindow(scene ?? new LaboScene()) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            if (scene is null && !ConfirmDiscardChanges()) return;
            CancelCanvasInteraction();
            if (scene is null) _filePath = null;
            if (scene is null) Editor.Text = dialog.Result;
            else using (Editor.Document.RunUpdate()) Editor.Document.Replace(0,Editor.Document.TextLength,dialog.Result);
            EngineSelector.SelectedItem = EngineSelector.Items.OfType<ComboBoxItem>().First(i => Equals(i.Tag,dialog.Engine));
            PreviewMode.IsChecked = true;
            UpdateTitle(); Editor.Focus();
        }
        catch (Exception exception) { MessageBox.Show(this,exception.Message + "\n\nYour source has not been changed.","PST-Labo",MessageBoxButton.OK,MessageBoxImage.Information); }
    }
}
