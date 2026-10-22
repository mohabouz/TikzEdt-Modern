using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace TikzEdt.Modern;

public partial class SettingsDialog : Window
{
    public IReadOnlyList<FontFamily> EditorFonts { get; }

    public SettingsDialog()
    {
        NativeWindowTheme.Attach(this);
        // Keep a configured fallback/custom family selectable even if it is not installed.
        EditorFonts = Fonts.SystemFontFamilies.Append(new FontFamily(SettingsStore.Current.EditorFont))
            .DistinctBy(font => font.Source, StringComparer.OrdinalIgnoreCase)
            .OrderBy(font => font.Source, StringComparer.CurrentCultureIgnoreCase).ToArray();
        InitializeComponent();
        DataContext = SettingsStore.Current.Clone();
        EditorAppearance.Apply(FontPreview, (UserSettings)DataContext);
    }

    private void EditorAppearance_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is not UserSettings settings || FontPreview is null) return;
        var preview = settings.Clone();
        // SelectionChanged can run before the SelectedValue binding writes its source.
        if (FontSelector.SelectedItem is FontFamily family) preview.EditorFont = family.Source;
        if (StyleSelector.SelectedItem is ComboBoxItem style) preview.EditorStyle = style.Content.ToString()!;
        if (double.TryParse(FontSizeField.Text, out var size) && double.IsFinite(size) && size is >= 8 and <= 72)
            preview.EditorFontSize = size;
        EditorAppearance.Apply(FontPreview, preview);
    }

    private void UpdateBindings(DependencyObject parent)
    {
        if (parent is TextBox textBox) textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            UpdateBindings(VisualTreeHelper.GetChild(parent, index));
    }

    private bool ValidateForm()
    {
        UpdateBindings(this);
        if (BindingGroup?.UpdateSources() == false || HasValidationErrors(this))
        {
            ErrorText.Text = "Some fields have invalid numbers. Correct the highlighted values.";
            return false;
        }
        try { ((UserSettings)DataContext).Validate(); ErrorText.Text = ""; return true; }
        catch (Exception exception) { ErrorText.Text = exception.Message; return false; }
    }

    private static bool HasValidationErrors(DependencyObject parent)
    {
        if (Validation.GetHasError(parent)) return true;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            if (HasValidationErrors(VisualTreeHelper.GetChild(parent, index))) return true;
        return false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateForm()) return;
        try { SettingsStore.Save((UserSettings)DataContext); DialogResult = true; }
        catch (Exception exception) { ErrorText.Text = exception.Message; }
    }

    private void PreviewStyle_Click(object sender, RoutedEventArgs e)
    {
        if (ValidateForm()) EditorAppearance.Apply(FontPreview, (UserSettings)DataContext);
    }

    private void Defaults_Click(object sender, RoutedEventArgs e)
    {
        DataContext = new UserSettings();
        EditorAppearance.Apply(FontPreview, (UserSettings)DataContext);
        ErrorText.Text = "Defaults restored. Choose Save settings to apply them.";
    }
}
