using System;
using System.Threading;
using System.Windows;

namespace EliteBioRadar
{
    public partial class App : Application
    {
        private Mutex? _instanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Enforce single instance — prevent cache corruption from concurrent runs
            _instanceMutex = new Mutex(true, "EliteBioRadar_SingleInstance", out bool isNewInstance);
            if (!isNewInstance)
            {
                MessageBox.Show(
                    "Elite Bio Radar is already running.\n\nOnly one instance can run at a time.",
                    "Elite Bio Radar",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

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

        protected override void OnExit(ExitEventArgs e)
        {
            _instanceMutex?.ReleaseMutex();
            _instanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
