using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace EliteBioRadar
{
    // "Looks wrong" on the body panel: opens a small window that shows the app's own render of the body beside a paste area for a game snip (Win+Shift+S,
    // then Ctrl+V). The app render is saved from what the window is actually showing on screen, and the snip is saved beside it, both named by the body,
    // so a pair can be matched without any naming by hand. (Rendering the shader scene off screen gave solid black files, so it is captured from the screen.)
    public partial class SystemScanWindow
    {
        // Where the pairs are saved: a "Wrong" folder inside the Planets folder the screenshots already go to, or beside the app if that folder does not exist.
        private static string VerdictFolder()
        {
            string planets = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "Pictures", "Claude", "Planets");
            string root = Directory.Exists(planets) ? planets : AppContext.BaseDirectory;
            string dir = System.IO.Path.Combine(root, "Wrong");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string VerdictFileStem(BodyScanDetail b)
        {
            string name = b.BodyName;
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private static string AppRenderPath(BodyScanDetail b) => System.IO.Path.Combine(VerdictFolder(), VerdictFileStem(b) + " - app.png");

        private static void SavePng(BitmapSource image, string path)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(image));
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            enc.Save(fs);
        }

        // Copies what is on screen under the element (the GPU-drawn result, the same thing the player sees) and saves it as a PNG. False if it is not showing.
        private static bool CaptureElementFromScreen(FrameworkElement el, string path)
        {
            try
            {
                if (!el.IsLoaded || el.ActualWidth < 2) return false;
                var src = PresentationSource.FromVisual(el);
                if (src?.CompositionTarget == null) return false;
                var m = src.CompositionTarget.TransformToDevice;
                var tl = el.PointToScreen(new Point(0, 0));
                int w = (int)Math.Round(el.ActualWidth * m.M11), h = (int)Math.Round(el.ActualHeight * m.M22);
                using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                    g.CopyFromScreen((int)Math.Round(tl.X), (int)Math.Round(tl.Y), 0, 0, new System.Drawing.Size(w, h));
                // Flat colour = nothing drawn there (covered or not painted yet).
                var first = bmp.GetPixel(0, 0); bool any = false;
                for (int y = 0; y < h && !any; y += Math.Max(1, h / 24))
                    for (int x = 0; x < w; x += Math.Max(1, w / 24))
                    {
                        var p = bmp.GetPixel(x, y);
                        if (Math.Abs(p.R - first.R) + Math.Abs(p.G - first.G) + Math.Abs(p.B - first.B) > 24) { any = true; break; }
                    }
                if (!any) return false;
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write($"Verdict: screen capture failed: {ex.Message}");
                return false;
            }
        }

        private Window? _pasteWindow;

        // A small always-on-top window next to the panel: shows the app's render (saved from the screen once it has drawn) beside a paste area, and saves a
        // clipboard image or a copied image file as "<body> - game.png".
        private void ShowPasteWindow(BodyScanDetail b, string appPath)
        {
            _pasteWindow?.Close();
            string gamePath = System.IO.Path.Combine(VerdictFolder(), VerdictFileStem(b) + " - game.png");
            const double view = 440;   // large, so the app render can be compared with a big game snip (was 250)

            var win = new Window
            {
                Title = "Looks wrong - add the game snip", Width = 1000, Height = 740, Owner = this, Topmost = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("#0a0f0f"), FontFamily = new FontFamily("Consolas"),
                ResizeMode = ResizeMode.CanResize, MinWidth = 960, MinHeight = 700,
            };
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(DText(EliteWatcherService.GetShortBodyName(b.BodyName, _watcher.StarSystem).ToUpperInvariant(), Brush("#cfe8e8"), 17, true));
            root.Children.Add(DText("1. In the game press Win+Shift+S and snip the body.\n2. Click Paste below (or press Ctrl+V here).", TextMid, 11.5, false, new Thickness(0, 6, 0, 8)));
            var status = DText("Saving the app render...", Brush("#ffb347"), 11.5, false, new Thickness(0, 0, 0, 8));
            root.Children.Add(status);

            var pair = new Grid();
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var appHost = new Grid { Width = view, Height = view, Background = Brush("#080d0d"), HorizontalAlignment = HorizontalAlignment.Center };
            appHost.Children.Add(BuildThumb(b, view));
            var appCol = new StackPanel();
            appCol.Children.Add(DText("app render", TextDim, 11, false, new Thickness(0, 0, 0, 3)));
            appCol.Children.Add(appHost);
            var preview = new Border { Width = view, Height = view, Background = Brush("#070b0b"), BorderBrush = BorderC, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, Child = DText("(the pasted snip shows here)", TextDim, 11.5) };
            ((TextBlock)preview.Child).VerticalAlignment = VerticalAlignment.Center;
            ((TextBlock)preview.Child).HorizontalAlignment = HorizontalAlignment.Center;
            var gameCol = new StackPanel();
            gameCol.Children.Add(DText("game snip", TextDim, 11, false, new Thickness(0, 0, 0, 3)));
            gameCol.Children.Add(preview);
            Grid.SetColumn(appCol, 0); Grid.SetColumn(gameCol, 1);
            pair.Children.Add(appCol); pair.Children.Add(gameCol);
            root.Children.Add(pair);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            Button B(string text) => new Button
            {
                Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), Background = Brush("#0a1616"), Foreground = Brush("#cfe8e8"),
                BorderBrush = BorderC, BorderThickness = new Thickness(1), FontFamily = new FontFamily("Consolas"), FontSize = 12, Cursor = Cursors.Hand, Template = DarkButtonTemplate(),
            };
            var paste = B("Paste from clipboard"); var done = B("Done");
            buttons.Children.Add(paste); buttons.Children.Add(done);
            root.Children.Add(buttons);
            win.Content = root;

            // Once the window has painted (the shader needs a moment), take the app render from the screen; retry a few times if it was not ready.
            int tries = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, __) =>
            {
                tries++;
                if (CaptureElementFromScreen(appHost, appPath))
                {
                    timer.Stop();
                    status.Text = "App render saved. Now paste the game snip."; status.Foreground = Brush("#7ee787");
                }
                else if (tries >= 6)
                {
                    timer.Stop();
                    status.Text = "The app render could not be saved (it did not draw). Paste the snip anyway.";
                    status.Foreground = Brush("#ff7b72");
                    Log.Write($"Verdict: app render of {b.BodyName} could not be captured from the screen");
                }
            };
            win.ContentRendered += (_, __) => timer.Start();

            void DoPaste()
            {
                try
                {
                    BitmapSource? img = null;
                    if (Clipboard.ContainsImage()) img = Clipboard.GetImage();
                    else if (Clipboard.ContainsFileDropList())
                    {
                        var f = Clipboard.GetFileDropList().Cast<string>().FirstOrDefault(p => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" }.Contains(System.IO.Path.GetExtension(p).ToLowerInvariant()));
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
            win.Closed += (_, __) => { timer.Stop(); if (_pasteWindow == win) _pasteWindow = null; };
            _pasteWindow = win;
            win.Show();
        }
    }
}
