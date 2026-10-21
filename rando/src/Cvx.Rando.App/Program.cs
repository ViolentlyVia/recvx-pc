using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace Cvx.Rando.App;

static class Program
{
    [STAThread]
    static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Geist and Geist Mono (SIL OFL) are embedded: fonts:Geist#Geist, fonts:Geist#Geist Mono.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .ConfigureFonts(fonts => fonts.AddFontCollection(
                new EmbeddedFontCollection(new Uri("fonts:Geist"), new Uri("avares://cvx-rando/Assets/Fonts"))))
            .With(new FontManagerOptions { DefaultFamilyName = "fonts:Geist#Geist" })
            .LogToTrace();
}
