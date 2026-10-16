using System.Diagnostics;
using Cvx.Formats;
using Cvx.Rando.Data;
using Cvx.Rando.Items;
using Cvx.Rando.Setup;

namespace Cvx.Rando.App;

/// <summary>The randomizer's window: settings, share code, generate and play.</summary>
public sealed class MainForm : Form
{
    private const string AppTitle = "CVX Randomizer";

    private readonly AppSettings _app;
    private readonly RandoData _data = RandoData.Load();
    private GameFolder? _game;
    private bool _loading;
    private bool _busy;
    private readonly ToolTip _tips = new();

    // game
    // (fixed-size labels that shorten with "...", so a long path cannot widen the window)
    private readonly Label _gamePath = new() { AutoEllipsis = true, Dock = DockStyle.Fill, Height = 24, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _gameStatus = new() { AutoEllipsis = true, Dock = DockStyle.Fill, Height = 24, TextAlign = ContentAlignment.MiddleLeft };

    // seed and share code
    private readonly NumericUpDown _seed = new() { Minimum = 0, Maximum = 999_999_999, Width = 110 };
    private readonly TextBox _shareCode = new() { Dock = DockStyle.Fill, MinimumSize = new Size(120, 0) };

    // items
    private readonly CheckBox _randomItems = Check("Randomize items");
    private readonly RadioButton _shuffle = new() { Text = "Shuffle the game's own items", AutoSize = true };
    private readonly RadioButton _custom = new() { Text = "New items from the amounts below", AutoSize = true };
    private readonly CheckBox _includeChris = Check("Include Chris's part");
    private readonly CheckBox _hiddenKeys = Check("Key items may be in out-of-the-way places");
    private readonly NumericUpDown _ratioAmmo = Ratio(), _ratioHealth = Ratio(), _ratioInk = Ratio(), _ratioPowder = Ratio();
    private readonly TrackBar _ammoAmount = new() { Minimum = 1, Maximum = 16, TickFrequency = 1, Width = 200 };
    private readonly CheckedListBox _weapons = new() { CheckOnClick = true, Width = 260, Height = 150, IntegralHeight = false };

    // inventory
    private readonly CheckBox _randomInventory = Check("Random starting items");
    private readonly ComboBox _weapon1 = Choice<WeaponChoice>(), _weapon2 = Choice<WeaponChoice>();
    private readonly CheckBox _sidePack = Check("Start with the side pack");
    private readonly CheckBox _noPoison = Check("Nosferatu does not poison");

    // enemies, doors, music
    private readonly CheckBox _randomEnemies = Check("Randomize enemies");
    private readonly ComboBox _enemyDifficulty = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly CheckBox _randomDoors = Check("Random doors");
    private readonly CheckBox _randomMusic = Check("Shuffle the music");

    // actions
    private readonly Button _generate = Btn("Generate"), _generatePlay = Btn("Generate and play"), _playNormal = Btn("Play normal game"),
        _openFolder = Btn("Open folder"), _spoiler = Btn("Spoiler log");
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 0, Width = 160, Visible = false };
    private readonly Label _status = new() { AutoEllipsis = true, Dock = DockStyle.Fill, Height = 24, TextAlign = ContentAlignment.MiddleLeft };

    public MainForm()
    {
        _app = AppSettings.Load();
        Text = $"{AppTitle} {Version}";
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            // no icon: the default one
        }
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 600);
        Size = new Size(820, 680);

        BuildLayout();
        _enemyDifficulty.Items.AddRange(["Easy", "Normal", "Hard", "Very hard"]);
        foreach (var w in _data.Items.Items.Where(i => i.IsWeapon && i.WeaponKind is { } k && k != "none").OrderBy(i => i.Id))
            _weapons.Items.Add(new WeaponItem(w));

        Show(_app.Rando);
        WireEvents();
        FindGame(_app.GameDirectory);
    }

    // ------------------------------------------------------------------ layout

    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true };
    private static NumericUpDown Ratio() => new() { Minimum = 0, Maximum = 32, Width = 60 };
    private static Button Btn(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };

    private static ComboBox Choice<T>() where T : struct, Enum
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        foreach (var v in Enum.GetValues<T>())
            box.Items.Add(v);
        return box;
    }

    private static FlowLayoutPanel Column(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(8) };
        panel.Controls.AddRange(controls);
        return panel;
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        panel.Controls.AddRange(controls);
        return panel;
    }

    private static Label Note(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(20, 2, 3, 8) };
    private static Label Caption(string text, int width = 150) => new() { Text = text, Width = width, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(3, 6, 3, 3) };

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Game
        var changeGame = Btn("Change...");
        changeGame.Click += (_, _) => ChooseGame();
        var game = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true };
        game.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        game.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        game.Controls.Add(_gamePath, 0, 0);
        game.Controls.Add(changeGame, 1, 0);
        game.Controls.Add(_gameStatus, 0, 1);
        game.SetColumnSpan(_gameStatus, 2);
        var gameBox = new GroupBox { Text = "Game", Dock = DockStyle.Fill, AutoSize = true };
        gameBox.Controls.Add(game);
        root.Controls.Add(gameBox);

        // Seed and share code
        var randomSeed = Btn("Random");
        randomSeed.Click += (_, _) => _seed.Value = Random.Shared.Next(1, 1_000_000);
        var copy = Btn("Copy");
        copy.Click += (_, _) => { Clipboard.SetText(_shareCode.Text); SetStatus("Share code copied."); };
        var paste = Btn("Paste");
        paste.Click += (_, _) => LoadShareCode(Clipboard.GetText());
        var seedRow = new TableLayoutPanel { ColumnCount = 7, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 6, 0, 6) };
        seedRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        seedRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        seedRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        seedRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        seedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        seedRow.Controls.Add(Caption("Seed", 40));
        seedRow.Controls.Add(_seed);
        seedRow.Controls.Add(randomSeed);
        seedRow.Controls.Add(Caption("Share code", 75));
        seedRow.Controls.Add(_shareCode);
        seedRow.Controls.Add(copy);
        seedRow.Controls.Add(paste);
        root.Controls.Add(seedRow);

        // Tabs
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Page("Items", Column(
            _randomItems,
            Row(_shuffle, _custom),
            _includeChris,
            Note("Chris's half has its own key items; without it, his part plays as in the normal game."),
            _hiddenKeys,
            new Label { Text = "New items: how often each kind comes up", AutoSize = true, Margin = new Padding(3, 10, 3, 3) },
            Row(Caption("Ammo", 70), _ratioAmmo, Caption("Healing", 70), _ratioHealth, Caption("Ink ribbons", 80), _ratioInk, Caption("Gunpowder", 75), _ratioPowder),
            Row(Caption("Ammo per pickup"), _ammoAmount),
            new Label { Text = "Weapons that may appear (none ticked: all)", AutoSize = true, Margin = new Padding(3, 10, 3, 3) },
            _weapons)));
        tabs.TabPages.Add(Page("Inventory", Column(
            _randomInventory,
            Note("Claire and Chris each start with a knife, the weapons below, and random ammo, healing items and maybe ink ribbons. Claire keeps her lighter."),
            Row(Caption("First weapon"), _weapon1),
            Row(Caption("Second weapon"), _weapon2),
            _sidePack,
            Note("Claire and Chris have the side pack's two extra item slots from the start."),
            _noPoison)));
        tabs.TabPages.Add(Page("Enemies", Column(
            _randomEnemies,
            Note("Ordinary enemies become other kinds, copied from other rooms: zombies, Cerberus dogs, bats, Black Widows, Bandersnatches, Hunters and Sweepers. Bosses, and enemies that take part in events, stay."),
            Row(Caption("Difficulty"), _enemyDifficulty))));
        tabs.TabPages.Add(Page("Doors", Column(
            _randomDoors,
            Note("Doors lead to other rooms. A new game starts in the second room, and its door leads to a room where you can save. Random doors also randomize the items, so that every seed can be finished."))));
        tabs.TabPages.Add(Page("Music", Column(
            _randomMusic,
            Note("Music tracks are swapped with others of their kind (area, save room, tension, boss). Event music stays."))));
        root.Controls.Add(tabs);

        // Actions
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 8, 0, 0) };
        actions.Controls.AddRange([_generate, _generatePlay, _playNormal, _openFolder, _spoiler]);
        root.Controls.Add(actions);

        var about = new LinkLabel { Text = "About", AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(3, 6, 3, 3) };
        about.LinkClicked += (_, _) => ShowAbout();
        // (not auto-sized: an auto-sized table would shrink the status text to nothing)
        var statusRow = new TableLayoutPanel { ColumnCount = 3, Dock = DockStyle.Fill, Height = 30, Margin = new Padding(0) };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        // explicit cells: a hidden control would otherwise give its cell to the next one
        statusRow.Controls.Add(_progress, 0, 0);
        statusRow.Controls.Add(_status, 1, 0);
        statusRow.Controls.Add(about, 2, 0);
        root.Controls.Add(statusRow);

        Controls.Add(root);
        AcceptButton = _generate;
    }

    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title) { AutoScroll = true, UseVisualStyleBackColor = true };
        page.Controls.Add(content);
        return page;
    }

    private void WireEvents()
    {
        foreach (var c in new Control[] { _seed, _ratioAmmo, _ratioHealth, _ratioInk, _ratioPowder })
            ((NumericUpDown)c).ValueChanged += (_, _) => SettingsChanged();
        foreach (var c in new CheckBox[] { _randomItems, _includeChris, _hiddenKeys, _randomInventory, _sidePack, _noPoison, _randomEnemies, _randomDoors, _randomMusic })
            c.CheckedChanged += (_, _) => SettingsChanged();
        _shuffle.CheckedChanged += (_, _) => SettingsChanged();
        _custom.CheckedChanged += (_, _) => SettingsChanged();
        _ammoAmount.ValueChanged += (_, _) => SettingsChanged();
        _weapons.ItemCheck += (_, _) => BeginInvoke(SettingsChanged);
        _weapon1.SelectedIndexChanged += (_, _) => SettingsChanged();
        _weapon2.SelectedIndexChanged += (_, _) => SettingsChanged();
        _enemyDifficulty.SelectedIndexChanged += (_, _) => SettingsChanged();
        _shareCode.Leave += (_, _) => { if (_shareCode.Text != ShareCode.Encode(Read())) LoadShareCode(_shareCode.Text); };

        _generate.Click += async (_, _) => await Generate(play: false);
        _generatePlay.Click += async (_, _) => await Generate(play: true);
        _playNormal.Click += (_, _) => PlayNormal();
        _openFolder.Click += (_, _) => OpenFolder();
        _spoiler.Click += (_, _) => OpenSpoiler();
        FormClosing += (_, e) =>
        {
            if (_busy)
            {
                e.Cancel = true;
                return;
            }
            _app.Rando = Read();
            _app.Save();
        };
    }

    // ------------------------------------------------------------------ settings <-> controls

    private void Show(RandoSettings s)
    {
        _loading = true;
        _seed.Value = Math.Clamp(s.Seed, (int)_seed.Minimum, (int)_seed.Maximum);
        _randomItems.Checked = s.RandomItems;
        _shuffle.Checked = s.ItemMode == ItemMode.Shuffle;
        _custom.Checked = s.ItemMode == ItemMode.Custom;
        _includeChris.Checked = s.IncludeChris;
        _hiddenKeys.Checked = s.HiddenKeyItems;
        _ratioAmmo.Value = Math.Clamp(s.RatioAmmo, 0, 32);
        _ratioHealth.Value = Math.Clamp(s.RatioHealth, 0, 32);
        _ratioInk.Value = Math.Clamp(s.RatioInkRibbons, 0, 32);
        _ratioPowder.Value = Math.Clamp(s.RatioGunpowder, 0, 32);
        _ammoAmount.Value = Math.Clamp(s.AmmoQuantity, 1, 16);
        for (var i = 0; i < _weapons.Items.Count; i++)
            _weapons.SetItemChecked(i, s.Weapons.Contains(((WeaponItem)_weapons.Items[i]).Info.Id));
        _randomInventory.Checked = s.RandomInventory;
        _weapon1.SelectedItem = s.StartWeapon1;
        _weapon2.SelectedItem = s.StartWeapon2;
        _sidePack.Checked = s.StartSidePack;
        _noPoison.Checked = s.NoNosferatuPoison;
        _randomEnemies.Checked = s.RandomEnemies;
        _enemyDifficulty.SelectedIndex = Math.Clamp(s.EnemyDifficulty, 0, 3);
        _randomDoors.Checked = s.RandomDoors;
        _randomMusic.Checked = s.RandomMusic;
        _loading = false;
        SettingsChanged();
    }

    private RandoSettings Read() => new()
    {
        Seed = (int)_seed.Value,
        RandomItems = _randomItems.Checked || _randomDoors.Checked,
        ItemMode = _custom.Checked ? ItemMode.Custom : ItemMode.Shuffle,
        IncludeChris = _includeChris.Checked,
        HiddenKeyItems = _hiddenKeys.Checked,
        RatioAmmo = (int)_ratioAmmo.Value,
        RatioHealth = (int)_ratioHealth.Value,
        RatioInkRibbons = (int)_ratioInk.Value,
        RatioGunpowder = (int)_ratioPowder.Value,
        AmmoQuantity = _ammoAmount.Value,
        Weapons = _weapons.CheckedItems.Cast<WeaponItem>().Select(w => w.Info.Id).Order().ToArray(),
        RandomInventory = _randomInventory.Checked,
        StartWeapon1 = _weapon1.SelectedItem is WeaponChoice w1 ? w1 : WeaponChoice.Sidearm,
        StartWeapon2 = _weapon2.SelectedItem is WeaponChoice w2 ? w2 : WeaponChoice.Random,
        StartSidePack = _sidePack.Checked,
        NoNosferatuPoison = _noPoison.Checked,
        RandomEnemies = _randomEnemies.Checked,
        EnemyDifficulty = Math.Max(0, _enemyDifficulty.SelectedIndex),
        RandomDoors = _randomDoors.Checked,
        RandomMusic = _randomMusic.Checked,
    };

    private void SettingsChanged()
    {
        if (_loading)
            return;
        // random doors always randomize items
        _randomItems.Enabled = !_randomDoors.Checked;
        if (_randomDoors.Checked)
            _randomItems.Checked = true;
        var items = _randomItems.Checked;
        foreach (var c in new Control[] { _shuffle, _custom, _includeChris, _hiddenKeys })
            c.Enabled = items;
        var custom = items && _custom.Checked;
        foreach (var c in new Control[] { _ratioAmmo, _ratioHealth, _ratioInk, _ratioPowder, _ammoAmount, _weapons })
            c.Enabled = custom;
        _weapon1.Enabled = _weapon2.Enabled = _randomInventory.Checked;
        _enemyDifficulty.Enabled = _randomEnemies.Checked;
        if (!_shareCode.Focused)
            _shareCode.Text = ShareCode.Encode(Read());
    }

    private void LoadShareCode(string text)
    {
        if (ShareCode.TryDecode(text, out var settings, out var error))
        {
            Show(settings!);
            SetStatus($"Loaded the settings of seed {settings!.Seed}.");
        }
        else
        {
            SetStatus(error ?? "That share code is not valid.");
            _shareCode.Text = ShareCode.Encode(Read());
        }
    }

    private sealed record WeaponItem(ItemInfo Info)
    {
        public override string ToString() => Info.Name;
    }

    // ------------------------------------------------------------------ game folder

    private void FindGame(string? remembered)
    {
        var dev = GameData.FindDefaultDirectory() is { } data ? Path.GetDirectoryName(Path.GetFullPath(data)) : null;
        _game = GameFolder.Find(remembered, AppContext.BaseDirectory) ?? GameFolder.Find(dev, AppContext.BaseDirectory);
        RefreshGame();
    }

    private void ChooseGame()
    {
        using var dialog = new FolderBrowserDialog { Description = "The folder with recvx.exe", UseDescriptionForTitle = true };
        if (_game != null)
            dialog.InitialDirectory = _game.Directory;
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        var folder = new GameFolder(dialog.SelectedPath);
        if (!folder.HasExe)
        {
            MessageBox.Show(this, $"There is no {GameFolder.ExeName} in that folder.", AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _game = folder;
        _app.GameDirectory = folder.Directory;
        _app.Save();
        RefreshGame();
    }

    private void RefreshGame()
    {
        if (_game == null)
        {
            _gamePath.Text = "Game folder not found";
            _gameStatus.Text = $"Choose the folder with {GameFolder.ExeName} (Change...).";
        }
        else
        {
            _gamePath.Text = _game.Directory;
            _tips.SetToolTip(_gamePath, _game.Directory);
            if (!_game.HasData)
                _gameStatus.Text = $"The game data is not set up yet: run {GameFolder.ConfigName} first.";
            else if (_game.ActiveSeed is { } seed)
                _gameStatus.Text = $"Randomizer on: seed {seed}. The game plays it until you choose \"Play normal game\".";
            else
                _gameStatus.Text = "Randomizer off: the game plays normally.";
        }
        var ready = _game is { HasData: true } && !_busy;
        _generate.Enabled = _generatePlay.Enabled = ready;
        _playNormal.Enabled = !_busy && _game?.ActiveMod != null;
        _openFolder.Enabled = _spoiler.Enabled = !_busy && _game != null;
    }

    // ------------------------------------------------------------------ actions

    /// <summary>
    /// Writes the seed and points the game at it. One seed at a time: once it is written, the
    /// other seed folders and the random run's saves are deleted (asked first).
    /// </summary>
    private async Task Generate(bool play)
    {
        if (_game is not { HasData: true } game)
            return;
        var settings = Read();
        var target = game.SeedDirectory(settings.Seed);
        if (GameRunning())
        {
            MessageBox.Show(this, "The game is running. Close it first: a new seed replaces the current one and its saves.", AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var replaces = game.SeedFolders().Count != 0 || game.HasRandomSaves;
        if (replaces &&
            MessageBox.Show(this, "This replaces your current seed: its folder and its saves (saves_rando) will be deleted. Continue?",
                AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _busy = true;
        _progress.Visible = true;
        _progress.MarqueeAnimationSpeed = 30;
        RefreshGame();
        var progress = new Progress<string>(SetStatus);
        var watch = Stopwatch.StartNew();
        try
        {
            var (result, stuck) = await Task.Run(() =>
            {
                using var rooms = RoomArchive.Open(new GameData(game.DataDirectory));
                var r = Generator.Generate(settings, _data, rooms, progress);
                Generator.WriteMod(r, rooms, target, progress);
                var old = game.DeleteOtherSeeds(settings.Seed);
                game.ClearRandomSaves();
                return (r, old);
            });
            game.Play(settings.Seed);
            _app.Rando = settings;
            _app.GameDirectory = game.Directory;
            _app.Save();
            SetStatus($"Seed {settings.Seed} is ready ({watch.Elapsed.TotalSeconds:0} s){(result.Enemies.Count != 0 ? $", enemies changed in {result.Enemies.Count} rooms" : "")}" +
                      (stuck.Count != 0 ? $"; {stuck.Count} old seed folder(s) could not be deleted." : "."));
            if (play)
                StartGame(game);
        }
        catch (ItemPlacementException ex)
        {
            SetStatus("This seed could not be made.");
            MessageBox.Show(this, $"{ex.Message}\n\nTry another seed or other settings.", AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (IOException ex)
        {
            SetStatus("Could not write the seed.");
            MessageBox.Show(this, $"Could not write the seed's files:\n{ex.Message}\n\nIs the game running? Close it and try again.", AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) when (ex is InvalidDataException or UnauthorizedAccessException)
        {
            SetStatus("Something went wrong.");
            MessageBox.Show(this, ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            _progress.MarqueeAnimationSpeed = 0;
            _progress.Visible = false;
            RefreshGame();
        }
    }

    private static bool GameRunning() => Process.GetProcessesByName("recvx").Length != 0;

    private void StartGame(GameFolder game)
    {
        try
        {
            Process.Start(new ProcessStartInfo(game.ExePath) { WorkingDirectory = game.Directory, UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            MessageBox.Show(this, $"Could not start the game:\n{ex.Message}", AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void PlayNormal()
    {
        _game?.PlayNormal();
        SetStatus("The game plays normally again, with your own saves.");
        RefreshGame();
    }

    private string? CurrentSeedFolder() =>
        _game == null ? null
        : _game.ActiveMod is { } mod && Directory.Exists(mod) ? mod
        : Directory.Exists(_game.SeedDirectory((int)_seed.Value)) ? _game.SeedDirectory((int)_seed.Value)
        : null;

    private void OpenFolder()
    {
        if (_game == null)
            return;
        var folder = CurrentSeedFolder() ?? _game.Directory;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = false });
    }

    private void OpenSpoiler()
    {
        var spoiler = CurrentSeedFolder() is { } folder ? Path.Combine(folder, "spoiler.txt") : null;
        if (spoiler == null || !File.Exists(spoiler))
        {
            SetStatus("No spoiler log yet: generate a seed first.");
            return;
        }
        if (MessageBox.Show(this, "The spoiler log shows where everything is. Open it?", AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            Process.Start(new ProcessStartInfo(spoiler) { UseShellExecute = true });
    }

    private static string Version =>
        typeof(MainForm).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

    private static string Resource(string name)
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream(name);
        return stream == null ? "" : new StreamReader(stream).ReadToEnd();
    }

    private void ShowAbout()
    {
        var answer = MessageBox.Show(this,
            $"{AppTitle} {Version}\n\nA randomizer for the PC port of Resident Evil Code: Veronica X (recvx-pc).\n\n" +
            "Based on BioRand by Ted John (IntelOrca), MIT licence, and its Code: Veronica room data, " +
            "with Code: Veronica information from kapdap.\n\n" +
            "Resident Evil Code: Veronica X is a trademark of Capcom. This program contains no game data.\n\n" +
            "Show the licences?",
            $"About {AppTitle}", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer != DialogResult.Yes)
            return;
        using var dialog = new Form
        {
            Text = "Licences", Size = new Size(720, 560), StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, ShowInTaskbar = false, Font = Font,
        };
        dialog.Controls.Add(new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            Text = (Resource("license.txt") + "\n\n" + Resource("notices.md")).ReplaceLineEndings("\r\n"),
        });
        dialog.ShowDialog(this);
    }

    private void SetStatus(string text) => _status.Text = text;
}
