// Biorand++: dark mode. The light look is BioRand's (the system's controls); dark merges
// Themes/Dark.xaml into the application's resources and darkens the windows' title bars.

#nullable enable

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace IntelOrca.Biohazard.BioRand
{
    internal static class Theme
    {
        private static readonly Uri DarkSource = new Uri("pack://application:,,,/cvx-rando;component/Themes/Dark.xaml");

        public static bool IsDark { get; private set; }

        /// <summary>Whether Windows has apps in dark mode (Settings, Personalization, Colors).</summary>
        public static bool WindowsIsDark()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void Apply(bool dark)
        {
            IsDark = dark;
            var merged = Application.Current.Resources.MergedDictionaries;
            var current = merged.FirstOrDefault(d => d.Source == DarkSource);
            if (dark && current == null)
                merged.Add(new ResourceDictionary { Source = DarkSource });
            else if (!dark && current != null)
                merged.Remove(current);
            foreach (Window window in Application.Current.Windows)
                SetTitleBar(window);
        }

        /// <summary>A dark title bar in dark mode (Windows 10 20H1 and newer; older ones keep theirs).</summary>
        public static void SetTitleBar(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;
            var value = IsDark ? 1 : 0;
            try
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
            }
            catch (Exception)
            {
                // (no dwmapi: the title bar stays as it is)
            }
        }

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
