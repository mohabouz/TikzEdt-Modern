using System.IO;
using System.Text;
using System.Windows;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Microsoft.Win32;

namespace TikzEdt.Modern;

public partial class PreambleDialog : Window
{
    private string? _selectedPath;

    public PreambleDialog()
    {
        NativeWindowTheme.Attach(this);
        InitializeComponent();
        PreambleEditor.TextArea.Caret.CaretBrush = (System.Windows.Media.Brush)FindResource("TextBrush");
        PreambleEditor.TextArea.SelectionBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");
        PreambleEditor.TextArea.SelectionForeground = System.Windows.Media.Brushes.White;
        LoadSyntaxHighlighting();
        EditorAppearance.Apply(PreambleEditor, SettingsStore.Current);
        LoadCurrentSelection();
    }

    private void LoadSyntaxHighlighting()
    {
        var syntaxPath = Path.Combine(AppContext.BaseDirectory, "TikzSyntax.xshd");
        if (!File.Exists(syntaxPath))
            return;

        using var reader = XmlReader.Create(syntaxPath);
        PreambleEditor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private void LoadCurrentSelection()
    {
        _selectedPath = PreambleSettings.SelectedFilePath;
        if (string.IsNullOrWhiteSpace(_selectedPath))
        {
            ShowBuiltInStarter();
            return;
        }

        LoadFile(_selectedPath);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a LaTeX preamble",
            Filter = "LaTeX preamble files|*.tex;*.sty|TeX files|*.tex|All files|*.*",
            CheckFileExists = true,
            InitialDirectory = GetInitialDirectory()
        };
        if (dialog.ShowDialog(this) == true)
            LoadFile(dialog.FileName);
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedPath))
            ShowBuiltInStarter();
        else
            LoadFile(_selectedPath);
    }

    private void UseBuiltIn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PreambleSettings.UseBuiltIn();
            DialogResult = true;
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void SaveAndUse_Click(object sender, RoutedEventArgs e)
    {
        var path = _selectedPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save the LaTeX preamble",
                Filter = "LaTeX preamble|*.tex|All files|*.*",
                DefaultExt = ".tex",
                AddExtension = true,
                FileName = "tikzedt-preamble.tex"
            };
            if (dialog.ShowDialog(this) != true)
                return;
            path = dialog.FileName;
        }

        try
        {
            File.WriteAllText(path, PreambleEditor.Text, new UTF8Encoding(false));
            PreambleSettings.UseFile(path);
            DialogResult = true;
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
    }

    private void LoadFile(string path)
    {
        try
        {
            var content = File.ReadAllText(path);
            _selectedPath = Path.GetFullPath(path);
            PathBox.Text = _selectedPath;
            PreambleEditor.Text = content;
            StatusText.Text = "Changes are written to this file when you choose Save and use.";
        }
        catch (Exception exception)
        {
            PathBox.Text = path;
            StatusText.Text = exception.Message;
            StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
    }

    private void ShowBuiltInStarter()
    {
        _selectedPath = null;
        PathBox.Text = "Built-in preamble";
        PreambleEditor.Text = PreambleSettings.StarterTemplate;
        StatusText.Text = "No external preamble file is selected.";
    }

    private string? GetInitialDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_selectedPath))
            return Path.GetDirectoryName(_selectedPath);
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}
