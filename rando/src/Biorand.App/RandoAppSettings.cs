// Ported from BioRand (biorand/classic, biorand/RandoAppSettings.cs), MIT, Copyright (c) 2022-2026 Ted John.
// Biorand++: changes marked "Biorand++".

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IntelOrca.Biohazard.BioRand
{
    public class RandoAppSettings
    {
        public string Seed1 { get; set; }
        public string Seed2 { get; set; }
        public string Seed3 { get; set; }
        public string SeedCv { get; set; }

        public int? LastSelectedGame { get; set; }

        public string GamePath1 { get; set; }
        public string GamePath2 { get; set; }
        public string GamePath3 { get; set; }
        public string GamePathCv { get; set; }

        public bool GameEnabled1 { get; set; }
        public bool GameEnabled2 { get; set; }
        public bool GameEnabled3 { get; set; }
        public bool GameEnabledCv { get; set; }

        // Biorand++: BioRand's folder (data, datapacks), read by BioRand's engine and the CVX randomizer
        public string BioRandPath { get; set; }
        public bool GameEnabledBioRand { get; set; } = true;

        public string GameExecutable1 { get; set; }
        public string GameExecutable2 { get; set; }
        public string GameExecutable3 { get; set; }

        public float BgmVolume1 { get; set; } = 0.75f;
        public float BgmVolume2 { get; set; } = 1.0f;
        public float BgmVolume3 { get; set; } = 0.75f;

        public bool DisableCustomContent { get; set; }

        public bool RandomizeTitleVoice { get; set; } = true;
        public bool MaxInventorySize { get; set; }
        public bool DoorSkip { get; set; }

        public string LastVersion { get; set; }


        public static string GetSettingsPath()
        {
            return Path.Combine(GetSettingsDirectory(), "settings.json");
        }

        public static string GetGenerationLogPath()
        {
            return Path.Combine(GetSettingsDirectory(), "generation.log");
        }

        public static string GetSettingsDirectory()
        {
            // Biorand++: its own settings, so a BioRand install keeps its own (BIORANDPP_SETTINGS: elsewhere, for tests)
            var env = Environment.GetEnvironmentVariable("BIORANDPP_SETTINGS");
            if (!string.IsNullOrEmpty(env))
                return env;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "biorandpp");
        }

        /// <summary>Biorand++: BioRand's settings folder (custom content lives there, for BioRand's engine).</summary>
        public static string GetBioRandSettingsDirectory()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "biorand");
        }

        public static string GetCustomContentDirectory()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "biorand", "data");
        }

        private static JsonSerializerOptions GetJsonSerializerOptions()
        {
            return new JsonSerializerOptions()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                ReadCommentHandling = JsonCommentHandling.Skip,
                WriteIndented = true
            };
        }

        public static RandoAppSettings Load()
        {
            try
            {
                var settingsPath = GetSettingsPath();
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    var instance = JsonSerializer.Deserialize<RandoAppSettings>(json, GetJsonSerializerOptions());
                    return instance;
                }
                else
                {
                    // Biorand++: the first time, BioRand's own settings (its game folders) when there are some
                    return ImportBioRandSettings() ?? AutoDetermineGames();
                }
            }
            catch
            {
            }
            return new RandoAppSettings();
        }

        public void Save()
        {
            try
            {
                var settingsPath = GetSettingsPath();
                var json = JsonSerializer.Serialize(this, GetJsonSerializerOptions());
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                File.WriteAllText(settingsPath, json);
            }
            catch
            {
            }
        }

        public static void LogGeneration(string seed, string gamePath)
        {
            var dt = DateTime.Now;

            var path = GetGenerationLogPath();
            using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write))
            {
                var sw = new StreamWriter(fs);
                sw.WriteLine($"Generated randomizer [{seed}] @ {dt} in \"{gamePath}\"");
                sw.Flush();
            }
        }

        /// <summary>
        /// Biorand++: BioRand's settings (%AppData%iorand\settings.json): the RE 1-3 folders,
        /// executables, music volumes and tweaks; its seeds are left behind (new ones are made).
        /// Then the CVX Randomizer's earlier settings and recvx-pc next to the program.
        /// </summary>
        private static RandoAppSettings ImportBioRandSettings()
        {
            try
            {
                var path = Path.Combine(GetBioRandSettingsDirectory(), "settings.json");
                if (!File.Exists(path))
                    return null;
                var bio = JsonSerializer.Deserialize<RandoAppSettings>(File.ReadAllText(path), GetJsonSerializerOptions());
                if (bio == null)
                    return null;
                var settings = new RandoAppSettings
                {
                    GamePath1 = bio.GamePath1,
                    GamePath2 = bio.GamePath2,
                    GamePath3 = bio.GamePath3,
                    GameEnabled1 = bio.GameEnabled1,
                    GameEnabled2 = bio.GameEnabled2,
                    GameEnabled3 = bio.GameEnabled3,
                    GameExecutable1 = bio.GameExecutable1,
                    GameExecutable2 = bio.GameExecutable2,
                    GameExecutable3 = bio.GameExecutable3,
                    BgmVolume1 = bio.BgmVolume1,
                    BgmVolume2 = bio.BgmVolume2,
                    BgmVolume3 = bio.BgmVolume3,
                    DisableCustomContent = bio.DisableCustomContent,
                    RandomizeTitleVoice = bio.RandomizeTitleVoice,
                    MaxInventorySize = bio.MaxInventorySize,
                    DoorSkip = bio.DoorSkip,
                    LastSelectedGame = bio.LastSelectedGame,
                };
                var location = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                if (File.Exists(Path.Combine(location, "recvx.exe")))
                {
                    settings.GameEnabledCv = true;
                    settings.GamePathCv = location;
                }
                ImportCvxRandoSettings(settings, location);
                return settings;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Biorand++: the first time, takes the CVX Randomizer's earlier window's settings
        /// (cvx-rando.json next to the program): the game and BioRand folders, and the CV:X
        /// settings as a share code.
        /// </summary>
        private static void ImportCvxRandoSettings(RandoAppSettings settings, string directory)
        {
            try
            {
                var path = Path.Combine(directory, "cvx-rando.json");
                if (!File.Exists(path))
                    return;
                var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
                var old = JsonSerializer.Deserialize<CvxRandoSettingsFile>(File.ReadAllText(path), options);
                if (old == null)
                    return;
                if (!string.IsNullOrEmpty(old.GameDirectory) && File.Exists(Path.Combine(old.GameDirectory, "recvx.exe")))
                {
                    settings.GameEnabledCv = true;
                    settings.GamePathCv = old.GameDirectory.TrimEnd(Path.DirectorySeparatorChar);
                }
                if (!string.IsNullOrEmpty(old.BioRandDirectory) && Directory.Exists(old.BioRandDirectory))
                    settings.BioRandPath = old.BioRandDirectory;
                if (old.Rando != null)
                    settings.SeedCv = Cvx.Rando.Setup.ShareCode.Encode(old.Rando);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
            }
        }

        private sealed class CvxRandoSettingsFile
        {
            public string GameDirectory { get; set; }
            public string BioRandDirectory { get; set; }
            public Cvx.Rando.RandoSettings Rando { get; set; }
        }

        private static RandoAppSettings AutoDetermineGames()
        {
            var settings = new RandoAppSettings();
            // Biorand++: a single-file program has no assembly location; recvx-pc's folder when it is here
            var location = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            if (File.Exists(Path.Combine(location, "recvx.exe")))
            {
                settings.GameEnabledCv = true;
                settings.GamePathCv = location;
            }
            ImportCvxRandoSettings(settings, location);
            var potentialExtractionDirectory = Path.GetDirectoryName(location);
            var directories = Directory.GetDirectories(potentialExtractionDirectory);
            foreach (var dir in directories)
            {
                var dirName = Path.GetFileName(dir);
                if (Regex.IsMatch(dirName, "re[123](hd)?"))
                {
                    var game = dirName[2] - '0';
                    switch (game)
                    {
                        case 1:
                            settings.GameEnabled1 = true;
                            settings.GamePath1 = dir;
                            settings.GameExecutable1 = "Bio.exe";
                            break;
                        case 2:
                            settings.GameEnabled2 = true;
                            settings.GamePath2 = dir;
                            settings.GameExecutable2 = "bio2 1.10.exe";
                            break;
                        case 3:
                            settings.GameEnabled3 = true;
                            settings.GamePath3 = dir;
                            settings.GameExecutable3 = "BIOHAZARD(R) 3 PC.exe";
                            break;
                    }
                }
            }
            return settings;
        }
    }
}
