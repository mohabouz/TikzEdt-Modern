using System.Windows;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private void ToolchainDoctor_Click(object sender, RoutedEventArgs e)
        => new ToolchainDoctorWindow(SettingsStore.Current) { Owner = this }.ShowDialog();
}
