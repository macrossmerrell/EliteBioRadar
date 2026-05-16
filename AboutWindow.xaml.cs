using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace EliteBioRadar
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            txtVersion.Text = version != null
                ? $"Version {version.Major}.{version.Minor}.{version.Build}"
                : "Version 1.0.0";
        }

        private void LnkFlaticon_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo("https://www.flaticon.com/free-icons/radar") { UseShellExecute = true }); }
            catch { }
        }

        private void LnkGitHub_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo("https://github.com/macrossmerrell/EliteBioRadar") { UseShellExecute = true }); }
            catch { }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
