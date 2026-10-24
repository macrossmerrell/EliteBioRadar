using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EliteBioRadar
{
    // "Looks wrong" on the body panel: saves the app's own render of the body as a PNG and opens a small paste window where the player drops a game
    // snip (Win+Shift+S, then Ctrl+V). Both files get the same system and body name, so a pair can be matched without any naming by hand.
    public partial class SystemScanWindow
    {
        // Where the pairs are saved: a "Wrong" folder inside the Planets folder the screenshots already go to, or beside the app if that folder does not exist.
        private static string VerdictFolder()
        {
            string planets = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "Pictures", "Claude", "Planets");
            string root = Directory.Exists(planets) ? planets : AppContext.BaseDirectory;
            string dir = Path.Combine(root, "Wrong");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string VerdictFileStem(BodyScanDetail b)
        {
            string name = b.BodyName;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private static void SavePng(BitmapSource image, string path)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(image));
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            enc.Save(fs);
        }

        // The app's render of the body at twice the thumbnail's size, saved as "<body> - app.png". Returns the path, or "" if it could not be rendered.
        private string SaveAppRender(BodyScanDetail b)
        {
            try
            {
                const int size = 380;
                var host = new Grid { Width = size, Height = size, Background = new SolidColorBrush(Color.FromRgb(0x08, 0x0d, 0x0d)) };
                host.Children.Add(BuildThumb(b, size));
                host.Measure(new Size(size, size));
                host.Arrange(new Rect(0, 0, size, size));
                host.UpdateLayout();
                var rtb = new RenderTargetBitmap(size * 2, size * 2, 192, 192, PixelFormats.Pbgra32);
                rtb.Render(host);
                string path = Path.Combine(VerdictFolder(), VerdictFileStem(b) + " - app.png");
                SavePng(rtb, path);
                return path;
            }
            catch (Exception ex)
            {
                Log.Write($"Verdict: app render could not be saved for {b.BodyName}: {ex.Message}");
                return "";
            }
        }

        private Window? _pasteWindow;

        // A small always-on-top window next to the panel: waits for an image on the clipboard (or a copied image file) and saves it as "<body> - game.png".
        private void ShowPasteWindow(BodyScanDetail b, string appPath)
        {
            _pasteWindow?.Close();
            string gamePath = Path.Combine(VerdictFolder(), VerdictFileStem(b) + " - game.png");

            var win = new Window
            {
                Title = "Looks wrong - add the game snip", Width = 420, Height = 420, Owner = this, Topmost = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("#0a0f0f"), FontFamily = new FontFamily("Consolas"),
                ResizeMode = ResizeMode.CanResize, MinWidth = 340, MinHeight = 320,
            };
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(DText(EliteWatcherService.GetShortBodyName(b.BodyName, _watcher.StarSystem).ToUpperInvariant(), Brush("#cfe8e8"), 17, true));
            root.Children.Add(DText("1. In the game press Win+Shift+S and snip the body.\n2. Click Paste below (or press Ctrl+V here).", TextMid, 11.5, false, new Thickness(0, 6, 0, 8)));
            var status = DText(string.IsNullOrEmpty(appPath) ? "App render could not be saved." : "App render saved.", Brush("#7ee787"), 11.5, false, new Thickness(0, 0, 0, 8));
            root.Children.Add(status);
            var preview = new Border { Height = 190, Background = Brush("#070b0b"), BorderBrush = BorderC, BorderThickness = new Thickness(1), Child = DText("(the pasted snip shows here)", TextDim, 11.5) };
            ((TextBlock)preview.Child).VerticalAlignment = VerticalAlignment.Center;
            ((TextBlock)preview.Child).HorizontalAlignment = HorizontalAlignment.Center;
            root.Children.Add(preview);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            Button B(string text) => new Button
            {
                Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), Background = Brush("#0a1616"), Foreground = Brush("#cfe8e8"),
                BorderBrush = BorderC, BorderThickness = new Thickness(1), FontFamily = new FontFamily("Consolas"), FontSize = 12, Cursor = Cursors.Hand, Template = DarkButtonTemplate(),
            };
            var paste = B("Paste from clipboard"); var done = B("Done");
            buttons.Children.Add(paste); buttons.Children.Add(done);
            root.Children.Add(buttons);
            win.Content = root;

            void DoPaste()
            {
                try
                {
                    BitmapSource? img = null;
                    if (Clipboard.ContainsImage()) img = Clipboard.GetImage();
                    else if (Clipboard.ContainsFileDropList())
                    {
                        var f = Clipboard.GetFileDropList().Cast<string>().FirstOrDefault(p => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" }.Contains(Path.GetExtension(p).ToLowerInvariant()));
                        if (f != null) img = new BitmapImage(new Uri(f));
                    }
                    if (img == null) { status.Text = "No image on the clipboard yet: take the snip, then paste."; status.Foreground = Brush("#ffb347"); return; }
                    SavePng(img, gamePath);
                    status.Text = "Game snip saved. You can tell me to go."; status.Foreground = Brush("#7ee787");
                    preview.Child = new Image { Source = img, Stretch = Stretch.Uniform, Margin = new Thickness(4) };
                }
                catch (Exception ex) { status.Text = "Could not paste: " + ex.Message; status.Foreground = Brush("#ff7b72"); }
            }
            paste.Click += (_, __) => DoPaste();
            done.Click += (_, __) => win.Close();
            win.PreviewKeyDown += (_, e) => { if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { DoPaste(); e.Handled = true; } };
            win.Closed += (_, __) => { if (_pasteWindow == win) _pasteWindow = null; };
            _pasteWindow = win;
            win.Show();
        }
    }
}
