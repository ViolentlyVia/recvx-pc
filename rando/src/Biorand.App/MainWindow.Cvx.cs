// Biorand++: the Code: Veronica X tile (game 3) on this repository's randomizer (Cvx.Rando)
// instead of BioRand's CV path. BioRand's window code calls in here where it would use
// BaseRandomiser and RandoConfig for game 3 (the hooks are marked "Biorand++" there).

#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Cvx.Formats;
using Cvx.Rando;
using Cvx.Rando.Data;
using Cvx.Rando.Items;
using Cvx.Rando.Music;
using Cvx.Rando.Npcs;
using Cvx.Rando.Setup;

namespace IntelOrca.Biohazard.BioRand
{
    public partial class MainWindow
    {
        private const int CvxGame = 3;

        private readonly RandoData _cvxData = RandoData.Load();
        private RandoSettings _cvx = new RandoSettings();
        private MusicLibrary? _cvxMusic;
        private CharacterLibrary? _cvxCharacters;
        private bool _cvxBusy;

        /// <summary>The weapons of the custom item mix, in the order of the weapon list.</summary>
        private IReadOnlyList<ItemInfo> CvxWeapons => _cvxData.Items.Items
            .Where(i => i.IsWeapon && i.WeaponKind is { } k && k != "none")
            .OrderBy(i => i.Id)
            .ToList();

        /// <summary>The music list: this game's own, then each game of the BioRand folder with music.</summary>
        private IReadOnlyList<string> CvxMusicGames =>
            [MusicLibrary.OwnGame, .. (_cvxMusic?.Games ?? []).Order(StringComparer.Ordinal)];

        private IReadOnlyList<LibraryCharacter> CvxCharacterList =>
            _cvxCharacters?.Characters.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList() ?? [];

        private GameFolder? CvxGameFolder =>
            !string.IsNullOrEmpty(_settings.GamePathCv) && File.Exists(Path.Combine(_settings.GamePathCv, GameFolder.ExeName))
                ? new GameFolder(_settings.GamePathCv)
                : null;

        // ------------------------------------------------------------------ settings and the seed

        /// <summary>The CVX settings saved last (a share code), or new ones with a random seed.</summary>
        private void LoadCvx()
        {
            _cvx = _settings.SeedCv is { } code && ShareCode.TryDecode(code, out var s, out _) && s != null
                ? s
                : new RandoSettings { Seed = _random.Next(0, 1_000_000) };
        }

        private void RandomizeCvxSeed()
        {
            _cvx = _cvx with { Seed = _random.Next(0, 1_000_000) };
            SaveSettings();
        }

        private static bool TryExtractCvxCode(string text, out RandoSettings? settings)
        {
            settings = null;
            var m = Regex.Match(text, Regex.Escape(ShareCode.Prefix) + @"[A-Za-z0-9_\-]+", RegexOptions.IgnoreCase);
            return m.Success && ShareCode.TryDecode(m.Value, out settings, out _) && settings != null;
        }

        /// <summary>The seed box takes a share code: a whole one replaces the settings.</summary>
        private void CvxSeedTextChanged()
        {
            if (TryExtractCvxCode(txtSeed.Text, out var settings) && ShareCode.Encode(settings!) != ShareCode.Encode(_cvx))
            {
                _cvx = settings!;
                SaveSettings();
                UpdateUi();
            }
        }

        // ------------------------------------------------------------------ the panel

        /// <summary>Shows or hides what only the CVX tile has, and what it does not have.</summary>
        private void ShowCvxControls(bool cvx)
        {
            var only = cvx ? Visibility.Visible : Visibility.Collapsed;
            var not = cvx ? Visibility.Collapsed : Visibility.Visible;
            panelCvxDoors.Visibility = only;
            chkCvxIncludeChris.Visibility = only;
            chkCvxSidePack.Visibility = only;
            chkCvxNoPoison.Visibility = only;
            chkPrioritiseCutscenes.Visibility = not;
            chkIncludeDocuments.Visibility = not;
            chkAlternativeRoute.Visibility = not;
            chkProtectSoftLock.Visibility = not;
            chkRandomEnemyPlacements.Visibility = not;
            listEnemies.Visibility = not;
            pieEnemies.Visibility = not;
            txtEnemiesNote.Visibility = not;
            // the character list's menu (BioRand's: Unselect All, Select All, Random) gets the RE2 cast
            _cvxRe2Cast ??= new MenuItem { Header = "Select RE2 Cast" };
            _cvxRe2Cast.Click -= CvxRe2Cast_Click;
            listNPCs.Menu.Items.Remove(_cvxRe2Cast);
            if (cvx)
            {
                _cvxRe2Cast.Click += CvxRe2Cast_Click;
                listNPCs.Menu.Items.Insert(0, _cvxRe2Cast);
            }
            // no enemy skins here: their column gives its room to the music and character lists
            gridLists.ColumnDefinitions[0].Width = cvx ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            // CVX's ammo amount is 1-16 (8 = the game's): one step each; BioRand's has 8 steps
            sliderAmmoQuantity.Maximum = cvx ? 15 : 7;
            // share codes grow with the picks: the box keeps the width of BioRand's seeds and scrolls
            txtSeed.MaxWidth = cvx ? 1250 : double.PositiveInfinity;
            txtSeed.Padding = cvx ? new Thickness(120, 0, 0, 0) : new Thickness(0);
            if (!cvx)
                btnPlayNormal.Visibility = Visibility.Collapsed;
        }

        /// <summary>The CVX tile's lists and labels (BioRand's <c>Update*</c> for its games).</summary>
        private void ShowCvxPanel()
        {
            _cvxMusic = MusicLibrary.Find(_settings.BioRandPath, AppContext.BaseDirectory, _settings.GamePathCv);
            _cvxCharacters = CharacterLibrary.Find(_settings.BioRandPath, AppContext.BaseDirectory, _settings.GamePathCv);

            // collapsed (BioRand hides them): the doors box moves up to the top of its column
            chkPlayer.Visibility = Visibility.Collapsed;
            chkCutscenes.Visibility = Visibility.Collapsed;
            chkEnemySkins.Visibility = Visibility.Hidden;
            chkRngBgm.Visibility = Visibility.Visible;
            sliderGunpowder.Visibility = Visibility.Visible;
            listWeapons.Names = CvxWeapons.Select(w => w.Name).ToArray();
            listBGMs.Names = CvxMusicGames.Select(MusicLibrary.DisplayName).ToArray();
            listBGMs.ToolTips = CvxMusicGames.Select(g => (object)(g == MusicLibrary.OwnGame ? "This game's own music"
                : $"{_cvxMusic!.Tracks.Count(t => t.Game == g)} tracks in BioRand")).ToArray();
            listNPCs.Names = CvxCharacterList.Select(c => c.Name).ToArray();
            listNPCs.ToolTips = CvxCharacterList.Select(c => (object)$"{c.Id} ({Path.GetFileName(c.Source)})").ToArray();

            var buttons = new[] { btnLog0, btnLog1 };
            for (var i = 0; i < 2; i++)
            {
                var hyperlink = (Hyperlink)buttons[i].Inlines.FirstInline;
                hyperlink.Inlines.Clear();
                hyperlink.Inlines.Add("Spoiler Log");
                buttons[i].Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            btnLog0.IsEnabled = CvxSpoilerPath() is { } spoiler && File.Exists(spoiler);
        }

        /// <summary>The settings into the controls (BioRand's <c>UpdateUi</c> for game 3).</summary>
        private void UpdateUiCvx()
        {
            using (SuspendEvents())
            {
                var s = _cvx;
                chkRngDoors.IsChecked = s.RandomDoors;
                sliderAreaCount.Value = Math.Clamp(s.DoorSegments, 1, 4) - 1;
                sliderAreaSize.Value = Math.Clamp(s.DoorSegmentSize, 1, 8) - 1;
                dropdownCvxCharacter.SelectedIndex = s.DoorCharacter == Character.Chris ? 1 : 0;

                chkRngItems.IsChecked = s.RandomItems || s.RandomDoors;
                chkHiddenKeyItems.IsChecked = s.HiddenKeyItems;
                chkCvxIncludeChris.IsChecked = s.IncludeChris;
                chkCustomItemPool.IsChecked = s.ItemMode == ItemMode.Custom;
                sliderGunpowder.Value = Math.Clamp(s.RatioGunpowder, 0, 31);
                sliderAmmo.Value = Math.Clamp(s.RatioAmmo, 0, 31);
                sliderHealth.Value = Math.Clamp(s.RatioHealth, 0, 31);
                sliderInkRibbons.Value = Math.Clamp(s.RatioInkRibbons, 0, 31);
                sliderAmmoQuantity.Value = CvxAmmoSlider(s.AmmoQuantity);
                var weapons = CvxWeapons;
                listWeapons.Values = weapons.Select(w => s.Weapons.Length == 0 || s.Weapons.Contains(w.Id)).ToArray();

                chkRandomInventory.IsChecked = s.RandomInventory;
                dropdownWeapon0.SelectedIndex = (int)s.StartWeapon1;
                dropdownWeapon1.SelectedIndex = (int)s.StartWeapon2;
                chkCvxSidePack.IsChecked = s.StartSidePack;

                chkRngEnemies.IsChecked = s.RandomEnemies;
                sliderEnemyDifficulty.Value = Math.Clamp(s.EnemyDifficulty, 0, 3);
                chkCvxNoPoison.IsChecked = s.NoNosferatuPoison;

                chkRngBgm.IsChecked = s.RandomMusic;
                var games = s.MusicGames.Select(g => g.ToLowerInvariant()).ToHashSet();
                listBGMs.Values = CvxMusicGames.Select(games.Contains).ToArray();

                chkRngChars.IsChecked = s.RandomNpcs;
                var picked = s.NpcCharacters.Select(c => c.ToLowerInvariant()).ToHashSet();
                listNPCs.Values = CvxCharacterList.Select(c => picked.Contains(c.Id)).ToArray();

                // BioRand's hints (completion estimate, item pie) read these
                _config.RandomDoors = s.RandomDoors;
                _config.AreaCount = (byte)(Math.Clamp(s.DoorSegments, 1, 4) - 1);
                _config.AreaSize = (byte)(Math.Clamp(s.DoorSegmentSize, 1, 8) - 1);
                _config.RatioGunpowder = (byte)sliderGunpowder.Value;
                _config.RatioAmmo = (byte)sliderAmmo.Value;
                _config.RatioHealth = (byte)sliderHealth.Value;
                _config.RatioInkRibbons = (byte)sliderInkRibbons.Value;

                var code = ShareCode.Encode(s);
                if (!txtSeed.Text.Contains(code, StringComparison.Ordinal))
                    txtSeed.Text = code;
                seedQrCode.Code = code;

                var game = CvxGameFolder;
                btnStartRE.Content = "Start CV:X";
                btnStartRE.IsEnabled = game != null && game.ActiveSeed != null;
                btnStartRE.ToolTip = game == null ? "Set the CV:X folder (recvx-pc) in the settings."
                    : game.ActiveSeed is { } active ? $"Start recvx.exe with seed {active}." : "Generate a seed first.";
                btnPlayNormal.Visibility = game?.ActiveSeed != null ? Visibility.Visible : Visibility.Collapsed;

                UpdateHints();
            }
            UpdateEnabledUi();
            panelRandomInventory.IsEnabled = true;
            chkCvxSidePack.IsEnabled = true;
        }

        /// <summary>The controls into the settings (BioRand's <c>UpdateConfig</c> for game 3).</summary>
        private void UpdateConfigCvx()
        {
            var weapons = CvxWeapons;
            var weaponValues = listWeapons.Values;
            var musicValues = listBGMs.Values;
            var characterValues = listNPCs.Values;
            var randomDoors = chkRngDoors.IsChecked == true;
            _cvx = _cvx with
            {
                RandomDoors = randomDoors,
                DoorSegments = (int)sliderAreaCount.Value + 1,
                DoorSegmentSize = (int)sliderAreaSize.Value + 1,
                DoorCharacter = dropdownCvxCharacter.SelectedIndex == 1 ? Character.Chris : Character.Claire,
                RandomItems = chkRngItems.IsChecked == true || randomDoors,
                HiddenKeyItems = chkHiddenKeyItems.IsChecked == true,
                IncludeChris = chkCvxIncludeChris.IsChecked == true,
                ItemMode = chkCustomItemPool.IsChecked == true ? ItemMode.Custom : ItemMode.Shuffle,
                RatioGunpowder = (int)sliderGunpowder.Value,
                RatioAmmo = (int)sliderAmmo.Value,
                RatioHealth = (int)sliderHealth.Value,
                RatioInkRibbons = (int)sliderInkRibbons.Value,
                AmmoQuantity = CvxAmmoQuantity((int)sliderAmmoQuantity.Value),
                // all ticked = all weapons (the settings' empty list)
                Weapons = weaponValues.Length == weapons.Count && weaponValues.All(v => v)
                    ? []
                    : weapons.Where((w, i) => i < weaponValues.Length && weaponValues[i]).Select(w => w.Id).ToArray(),
                RandomInventory = chkRandomInventory.IsChecked == true,
                StartWeapon1 = (WeaponChoice)Math.Max(0, dropdownWeapon0.SelectedIndex),
                StartWeapon2 = (WeaponChoice)Math.Max(0, dropdownWeapon1.SelectedIndex),
                StartSidePack = chkCvxSidePack.IsChecked == true,
                RandomEnemies = chkRngEnemies.IsChecked == true,
                EnemyDifficulty = (int)sliderEnemyDifficulty.Value,
                NoNosferatuPoison = chkCvxNoPoison.IsChecked == true,
                RandomMusic = chkRngBgm.IsChecked == true,
                MusicGames = CvxMusicGames.Where((g, i) => i < musicValues.Length && musicValues[i])
                    .Concat(MissingCvx(_cvx.MusicGames, CvxMusicGames))
                    .OrderBy(g => g == MusicLibrary.OwnGame ? 0 : 1).ThenBy(g => g, StringComparer.Ordinal).ToArray(),
                RandomNpcs = chkRngChars.IsChecked == true,
                NpcCharacters = CvxCharacterList.Where((c, i) => i < characterValues.Length && characterValues[i]).Select(c => c.Id)
                    .Concat(MissingCvx(_cvx.NpcCharacters, CvxCharacterList.Select(c => c.Id).ToList()))
                    .Order(StringComparer.Ordinal).ToArray(),
                RandomTitleCall = chkRandomizeTitleVoice.IsChecked == true,
            };
            SaveSettings();
        }

        /// <summary>Picks of a share code that this computer's BioRand folder does not have: kept, so the code stays the same.</summary>
        private static IEnumerable<string> MissingCvx(IEnumerable<string> picked, IReadOnlyList<string> known) =>
            picked.Select(p => p.ToLowerInvariant()).Where(p => !known.Contains(p, StringComparer.OrdinalIgnoreCase));

        /// <summary>The ammo slider on the CVX tile: 16 steps for CVX's amounts 1-16 (8 = the game's amounts).</summary>
        internal static int CvxAmmoQuantity(int slider) => Math.Clamp(slider, 0, 15) + 1;

        internal static int CvxAmmoSlider(int quantity) => Math.Clamp(quantity, 1, 16) - 1;

        private MenuItem? _cvxRe2Cast;

        private void CvxRe2Cast_Click(object sender, RoutedEventArgs e)
        {
            listNPCs.Values = CvxCharacterList.Select(c => c.Id.EndsWith(".re2", StringComparison.Ordinal)).ToArray();
            UpdateConfig();
            UpdateUi();
        }

        // ------------------------------------------------------------------ generate, play, logs

        private string? CvxSpoilerPath() =>
            CvxGameFolder is { ActiveSeed: { } seed } game && int.TryParse(seed, out var n)
                ? Path.Combine(game.SeedDirectory(n), "spoiler.txt")
                : null;

        private static bool CvxGameRunning() => Process.GetProcessesByName("recvx").Length != 0;

        private async void GenerateCvx(Button button)
        {
            if (_cvxBusy)
                return;
            SaveSettings();
            if (CvxGameFolder is not { } game || !game.HasData)
            {
                ShowGenerateFailedMessage("You have not specified a CV:X folder (recvx-pc, with recvx.exe and its data) in the settings.");
                return;
            }
            if (CvxGameRunning())
            {
                ShowFailedMessage("The game is running", "Close it first: a new seed replaces the current one and its saves.");
                return;
            }
            var replaces = game.SeedFolders().Count != 0 || game.HasRandomSaves;
            if (replaces && MessageBox.Show(this,
                    "Its folder and the random run's saves (saves_rando) will be deleted. Your own saves are not touched.",
                    game.ActiveSeed is { } active ? $"Replace seed {active}?" : "Replace the current seed?",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            var settings = _cvx;
            var target = game.SeedDirectory(settings.Seed);
            var doorSkip = chkDoorSkip.IsChecked == true;
            var progress = new Progress<string>(m => progressLabel.Text = m);
            _cvxBusy = true;
            try
            {
                button.Content = "Generating...";
                IsEnabled = false;
                var watch = Stopwatch.StartNew();
                var (result, stuck) = await Task.Run(() =>
                {
                    using var rooms = RoomArchive.Open(new Cvx.Formats.GameData(game.DataDirectory));
                    var r = Cvx.Rando.Generator.Generate(settings, _cvxData, rooms, progress, _cvxMusic, _cvxCharacters);
                    Cvx.Rando.Generator.WriteMod(r, rooms, target, progress);
                    var old = game.DeleteOtherSeeds(settings.Seed);
                    game.ClearRandomSaves();
                    return (r, old);
                });
                game.Play(settings.Seed);
                if (doorSkip)
                {
                    var ini = game.ReadIni();
                    ini.Set("skip_doors", "1");
                    ini.Save();
                }
                RandoAppSettings.LogGeneration(ShareCode.Encode(settings), game.Directory);
                progressLabel.Text = $"Seed {settings.Seed} is ready ({watch.Elapsed.TotalSeconds:0} s)" +
                                     (stuck.Count != 0 ? $"; {stuck.Count} old seed folder(s) could not be deleted." : ".");
                MessageBox.Show(this,
                    $"Seed {settings.Seed} is in the game's rando folder and active: press Start CV:X (or run recvx.exe) to play it." +
                    "\n\nClick Spoiler Log to see where everything went.",
                    "Randomization Complete!", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (ItemPlacementException ex)
            {
                progressLabel.Text = "This seed could not be made.";
                ShowFailedMessage("This seed could not be made", $"{ex.Message}\n\nTry another seed or other settings.");
            }
            catch (IOException ex)
            {
                progressLabel.Text = "Could not write the seed.";
                ShowFailedMessage("Could not write the seed", $"Could not write the seed's files:\n{ex.Message}\n\nIs the game running? Close it and try again.");
            }
            catch (Exception ex) when (ex is InvalidDataException or UnauthorizedAccessException)
            {
                progressLabel.Text = "Something went wrong.";
                ShowFailedMessage("Something went wrong", ex.Message);
            }
            finally
            {
                button.Content = "Generate";
                IsEnabled = true;
                _cvxBusy = false;
                ShowCvxPanel();
                UpdateUi();
            }
        }

        private void StartCvx()
        {
            if (CvxGameFolder is not { } game)
                return;
            try
            {
                Process.Start(new ProcessStartInfo(game.ExePath) { WorkingDirectory = game.Directory, UseShellExecute = false });
            }
            catch (Exception ex) when (ex is Win32Exception or IOException)
            {
                ShowFailedMessage("Unable to start the game", ex.Message);
            }
        }

        private void PlayNormal_Click(object sender, RoutedEventArgs e)
        {
            if (CvxGameFolder is not { } game)
                return;
            game.PlayNormal();
            progressLabel.Text = "The game plays normally again, with your own saves.";
            UpdateUi();
        }

        private void ViewCvxSpoiler()
        {
            if (CvxSpoilerPath() is { } path && File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
                ShowFailedMessage("View Log", "No spoiler log: generate a seed first.");
        }

        // ------------------------------------------------------------------ the settings page

        private void gameLocationCV_Validate(object sender, PathValidateEventArgs e)
        {
            if (!Directory.Exists(e.Path))
            {
                e.Message = "Directory does not exist!";
                e.IsValid = false;
                return;
            }
            var game = new GameFolder(e.Path);
            if (!game.HasExe)
            {
                e.Message = $"No {GameFolder.ExeName} here: choose recvx-pc's folder.";
                e.IsValid = false;
                return;
            }
            if (!game.HasData)
            {
                e.Message = "recvx.exe found, but not the game's data (data\\files.idx, RDX_LNK.AFS).";
                e.IsValid = false;
                return;
            }
            e.IsValid = true;
            e.Message = "Directory looks good!";
        }

        private void gameLocationBioRand_Validate(object sender, PathValidateEventArgs e)
        {
            if (!HasBioRandData(e.Path))
            {
                e.Message = "No data or datapacks folder here: choose BioRand's folder.";
                e.IsValid = false;
                return;
            }
            e.IsValid = true;
            e.Message = "BioRand's data found!";
        }

        private static bool HasBioRandData(string? path) =>
            !string.IsNullOrEmpty(path) && (Directory.Exists(Path.Combine(path, "data")) || Directory.Exists(Path.Combine(path, "datapacks")));

        /// <summary>
        /// BioRand's folder (its data and datapacks): the one chosen, else a "biorand" folder next
        /// to or above this program or a game's folder.
        /// </summary>
        private string? FindBioRandFolder()
        {
            if (!_settings.GameEnabledBioRand)
                return null;
            if (HasBioRandData(_settings.BioRandPath))
                return _settings.BioRandPath;
            foreach (var start in new[] { AppContext.BaseDirectory, _settings.GamePathCv, _settings.GamePath1, _settings.GamePath2, _settings.GamePath3 })
            {
                for (var dir = string.IsNullOrEmpty(start) || !Directory.Exists(start) ? null : new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    foreach (var name in new[] { "biorand", "BioRand" })
                    {
                        var candidate = Path.Combine(dir.FullName, name);
                        if (HasBioRandData(candidate))
                            return candidate;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Points BioRand's engine at the BioRand folder's data (<c>BIORAND_DATA</c>, read by
        /// <c>BaseRandomiser.CreateDataManager</c>): custom content first when enabled, as BioRand
        /// does, then the datapacks, then the data folder.
        /// </summary>
        private void UseBioRandData()
        {
            var folder = FindBioRandFolder();
            if (folder != null && string.IsNullOrEmpty(_settings.BioRandPath))
            {
                // found next to the program or a game: shown in the settings
                _settings.BioRandPath = folder;
                gameLocationBioRand.Location = folder;
            }
            var paths = new List<string>();
            if (!_settings.DisableCustomContent)
            {
                paths.Add(Path.Combine(RandoAppSettings.GetBioRandSettingsDirectory(), "data"));
                paths.Add(Path.Combine(RandoAppSettings.GetBioRandSettingsDirectory(), "datapacks", "*"));
            }
            if (folder != null)
            {
                paths.Add(Path.Combine(folder, "datapacks", "*"));
                paths.Add(Path.Combine(folder, "data"));
            }
            Environment.SetEnvironmentVariable("BIORAND_DATA", string.Join(Path.PathSeparator, paths.Where(p =>
                Directory.Exists(p.EndsWith("*", StringComparison.Ordinal) ? Path.GetDirectoryName(p) : p))));
        }

        private void gameLocationBioRand_Changed(object sender, EventArgs e)
        {
            SaveSettings();
            UseBioRandData();
            _randomizer = null;
            PopulateContributors();
        }

        private bool HasBioRandEngineData => FindBioRandFolder() is { } f && Directory.Exists(Path.Combine(f, "data"));
    }
}
