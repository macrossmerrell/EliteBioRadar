using System;
using System.Windows;

namespace EliteBioRadar
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            Log.Write("App.OnStartup begin");
            base.OnStartup(e);
            Log.Write("App.OnStartup base done");

            try
            {
                Log.Write("Creating MainWindow...");
                var window = new MainWindow();
                Log.Write("Calling window.Show()...");
                window.Show();
                Log.Write("window.Show() returned");
            }
            catch (Exception ex)
            {
                Log.Write($"OnStartup EXCEPTION: {ex}");
                MessageBox.Show($"Fatal startup error:\n{ex}", "Elite Bio Radar");
                Shutdown();
            }
        }
    }
}
