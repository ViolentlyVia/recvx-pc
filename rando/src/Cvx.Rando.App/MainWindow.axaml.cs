using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Cvx.Rando.App;

/// <summary>The window: layout 1b of the design handoff, with in-window dialogs.</summary>
public sealed partial class MainWindow : Window, IAppHost
{
    private static readonly IBrush TitleBrush = Brush.Parse("#ececee"), WarnTitleBrush = Brush.Parse("#e9c27a");

    private readonly MainViewModel _vm;
    private TaskCompletionSource<bool>? _dialog;
    private PixelPoint _normalPosition;
    private Size _normalSize;

    // for the XAML previewer
    public MainWindow() : this(new AppSettings(), new MainViewModel(new AppSettings())) { }

    public MainWindow(AppSettings settings, MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.Attach(this);
        Place(settings.Window);

        SeedBox.LostFocus += (_, _) => vm.CommitSeedText();
        ShareBox.GotFocus += (_, _) => { vm.EditShareCode(true); ShareShown.IsVisible = false; ShareBox.ClearValue(ForegroundProperty); };
        ShareBox.LostFocus += (_, _) => LeaveShareBox();
        ShareBox.Foreground = Brushes.Transparent;   // the shortened copy is shown on top until the box is focused
        ShareBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                // Enter loads a pasted code here instead of generating
                vm.EditShareCode(false);
                vm.EditShareCode(true);
                e.Handled = true;
            }
        };

        AboutButton.Click += async (_, _) => await ShowAbout();
        DialogOk.Click += (_, _) => CloseDialog(true);
        DialogCancel.Click += (_, _) => CloseDialog(false);
        LicenceClose.Click += (_, _) => LicenceLayer.IsVisible = false;

        PositionChanged += (_, _) => RememberNormalBounds();
        SizeChanged += (_, _) => RememberNormalBounds();
        Closing += (_, e) =>
        {
            if (_vm.IsBusy)
            {
                e.Cancel = true;
                return;
            }
            RememberNormalBounds();
            _vm.SaveOnClose(new WindowPlacement(_normalPosition.X, _normalPosition.Y, _normalSize.Width, _normalSize.Height,
                WindowState == WindowState.Maximized));
        };
    }

    // ------------------------------------------------------------------ window placement

    private void Place(WindowPlacement? placement)
    {
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (placement == null || placement.Width < MinWidth || placement.Height < MinHeight)
            return;
        // only where a screen still is (a monitor may have gone)
        var centre = new PixelPoint(placement.X + (int)(placement.Width / 2), placement.Y + 20);
        if (Screens.ScreenFromPoint(centre) == null)
            return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        _normalPosition = new PixelPoint(placement.X, placement.Y);
        _normalSize = new Size(placement.Width, placement.Height);
        Position = new PixelPoint(placement.X, placement.Y);
        Width = placement.Width;
        Height = placement.Height;
        if (placement.Maximized)
            WindowState = WindowState.Maximized;
    }

    private void RememberNormalBounds()
    {
        if (WindowState != WindowState.Normal || !IsVisible)
            return;
        _normalPosition = Position;
        _normalSize = new Size(Width, Height);
    }

    // ------------------------------------------------------------------ keys

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;
        if (LicenceLayer.IsVisible)
        {
            if (e.Key == Key.Escape)
            {
                LicenceLayer.IsVisible = false;
                e.Handled = true;
            }
            return;
        }
        if (DialogLayer.IsVisible)
        {
            if (e.Key == Key.Escape)
            {
                CloseDialog(false);
                e.Handled = true;
            }
            return;
        }
        // Enter = Generate (the old AcceptButton)
        if (e.Key == Key.Enter && _vm.CanGenerate)
        {
            e.Handled = true;
            _ = _vm.Generate(play: false);
        }
    }

    private async void OnGenerate(object? sender, RoutedEventArgs e) => await _vm.Generate(play: false);
    private async void OnGeneratePlay(object? sender, RoutedEventArgs e) => await _vm.Generate(play: true);

    private void LeaveShareBox()
    {
        _vm.EditShareCode(false);
        ShareShown.IsVisible = true;
        ShareBox.Foreground = Brushes.Transparent;
    }

    // ------------------------------------------------------------------ dialogs (IAppHost)

    private Task<bool> OpenDialog(string title, string body, string ok, string? cancel, bool warning)
    {
        _dialog?.TrySetResult(false);
        _dialog = new TaskCompletionSource<bool>();
        DialogTitle.Text = title;
        DialogTitle.Foreground = warning ? WarnTitleBrush : TitleBrush;
        DialogBody.Text = body;
        DialogOk.Content = ok;
        DialogCancel.Content = cancel;
        DialogCancel.IsVisible = cancel != null;
        DialogLayer.IsVisible = true;
        DialogOk.Focus(NavigationMethod.Tab);
        return _dialog.Task;
    }

    private void CloseDialog(bool result)
    {
        DialogLayer.IsVisible = false;
        var dialog = _dialog;
        _dialog = null;
        dialog?.TrySetResult(result);
    }

    public Task<bool> Confirm(string title, string body, string ok) => OpenDialog(title, body, ok, "Cancel", warning: false);

    public Task Message(string title, string body, bool warning = false) => OpenDialog(title, body, "OK", null, warning);

    public async Task<string?> PickFolder(string title, string? start)
    {
        var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
        if (start != null && await StorageProvider.TryGetFolderFromPathAsync(start) is { } folder)
            options.SuggestedStartLocation = folder;
        var picked = await StorageProvider.OpenFolderPickerAsync(options);
        return picked.Count == 0 ? null : picked[0].TryGetLocalPath();
    }

    public async Task<string?> GetClipboardText() => Clipboard is { } c ? await c.TryGetTextAsync() : null;

    public async Task SetClipboardText(string text)
    {
        if (Clipboard is { } c)
            await c.SetTextAsync(text);
    }

    // ------------------------------------------------------------------ about

    private static string Resource(string name)
    {
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream(name);
        return stream == null ? "" : new StreamReader(stream).ReadToEnd();
    }

    private async Task ShowAbout()
    {
        var licences = await OpenDialog($"{MainViewModel.AppTitle} {MainViewModel.Version}",
            "A randomizer for the PC port of Resident Evil Code: Veronica X (recvx-pc).\n\n" +
            "Based on BioRand by Ted John (IntelOrca), MIT licence, and its Code: Veronica room data, " +
            "with Code: Veronica information from kapdap.\n\n" +
            "Resident Evil Code: Veronica X is a trademark of Capcom. This program contains no game data.\n\n" +
            "Show the licences?",
            "Show the licences", "Close", warning: false);
        if (!licences)
            return;
        LicenceText.Text = (Resource("license.txt") + "\n\n" + Resource("notices.md") + "\n\n" + Resource("notices-skiasharp.txt"))
            .ReplaceLineEndings("\n");
        LicenceLayer.IsVisible = true;
        LicenceClose.Focus(NavigationMethod.Tab);
    }
}
