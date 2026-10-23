// Biorand++: a testing aid. BIORANDPP_SHOTS=<folder> saves a picture of the window with each
// tile selected (game_0.png ... game_3.png, settings.png, info.png, new.png), then closes. On the
// CV:X tile it also checks that the window gives back the settings it shows (cvx_roundtrip.txt).

#nullable enable

using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IntelOrca.Biohazard.BioRand
{
    public partial class MainWindow
    {
        private void TakeShotsIfAsked()
        {
            var folder = Environment.GetEnvironmentVariable("BIORANDPP_SHOTS");
            if (string.IsNullOrEmpty(folder))
                return;
            Directory.CreateDirectory(folder);
            var tiles = new[] { (0, "game_0"), (1, "game_1"), (2, "game_2"), (3, "game_3"), (-1, "settings"), (-2, "info"), (-3, "new") };
            var next = 0;
            void Shoot()
            {
                if (next == tiles.Length)
                {
                    Application.Current.Shutdown();
                    return;
                }
                var (game, name) = tiles[next++];
                SelectedGame = game;
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    UpdateLayout();
                    var content = (FrameworkElement)Content;
                    var dpi = VisualTreeHelper.GetDpi(this);
                    var bitmap = new RenderTargetBitmap((int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
                        dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                    var background = new DrawingVisual();
                    using (var dc = background.RenderOpen())
                        dc.DrawRectangle(SystemColors.WindowBrush, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                    bitmap.Render(background);
                    bitmap.Render(content);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var fs = File.Create(Path.Combine(folder, name + ".png")))
                        encoder.Save(fs);
                    if (game == CvxGame)
                    {
                        var before = Cvx.Rando.Setup.ShareCode.Encode(_cvx);
                        UpdateConfigCvx();
                        var after = Cvx.Rando.Setup.ShareCode.Encode(_cvx);
                        File.WriteAllText(Path.Combine(folder, "cvx_roundtrip.txt"),
                            (before == after ? "same" : "DIFFERENT") + $"\nshown: {before}\nread:  {after}\n");
                    }
                    Shoot();
                }));
            }
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Shoot));
        }
    }
}
