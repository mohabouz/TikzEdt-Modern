using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private bool TryEditChemfigRange(int offset, int length)
    {
        if (offset < 0 || offset + length > Editor.Text.Length) return false;
        var relative = Editor.Text.Substring(offset, length).IndexOf("\\chemfig{", StringComparison.Ordinal);
        if (relative < 0) return false;
        CancelCanvasInteraction();
        Editor.CaretOffset = offset + relative + 9;
        Chemfig_Click(this, new RoutedEventArgs());
        return true;
    }
    private void Chemfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var target = ChemfigMolecule.FindCommand(Editor.Text, Editor.CaretOffset);
            var molecule = target is { } existing ? ChemfigMolecule.Parse(existing.Body) : new ChemfigMolecule();
            if (molecule.Atoms.Count == 0) molecule.Atoms.Add(new("", new Point()));
            var dialog = new ChemfigEditorWindow(molecule, ((ComboBoxItem)EngineSelector.SelectedItem).Tag.ToString()!,
                PreambleSettings.ReadSnapshot().Content ?? "", _filePath is null ? null : Path.GetDirectoryName(_filePath)) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            if (target is { } replacement) Editor.Document.Replace(replacement.Offset, replacement.Length, dialog.Result);
            else InsertTikzCommand("\\node[inner sep=0pt] at (0,0) {" + dialog.Result + "};");
            Editor.Focus();
        }
        catch (Exception exception)
        { MessageBox.Show(this, exception.Message + "\n\nThe source has not been changed. Place the caret outside the command to create a new molecule.", "Chemfig visual editor", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
}
