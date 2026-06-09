using System.Collections.Generic;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace SwitchPlaytimeExophase.Settings
{
    public class SwitchPlaytimeSettings : ObservableObject
    {
        private string username = string.Empty;

        private bool importSwitch1 = true;
        private bool importSwitch2 = true;

        private bool addMissingGames = true;
        private bool updateExistingGames = true;
        private bool onlyMatchSwitchPlatform = true;

        private bool importPlaytime = true;
        private bool overwriteExistingPlaytime = true;

        private bool importLastActivity = true;

        private bool downloadMetadata = true;
        private bool overwriteMetadata = false;

        private bool skipDemos = false;
        private int minPlaytimeMinutes = 0;

        // ---- Exophase account ----

        /// <summary>Global Exophase account username (not the Nintendo account name).</summary>
        public string Username
        {
            get => username;
            set => SetValue(ref username, value);
        }

        // ---- Platforms ----

        /// <summary>Include games listed on the original Nintendo Switch.</summary>
        public bool ImportSwitch1
        {
            get => importSwitch1;
            set => SetValue(ref importSwitch1, value);
        }

        /// <summary>Include games listed on Nintendo Switch 2.</summary>
        public bool ImportSwitch2
        {
            get => importSwitch2;
            set => SetValue(ref importSwitch2, value);
        }

        // ---- Library actions ----

        /// <summary>Create new entries for Switch games not already in Playnite.</summary>
        public bool AddMissingGames
        {
            get => addMissingGames;
            set => SetValue(ref addMissingGames, value);
        }

        /// <summary>Update games that already exist in Playnite (matched by name).</summary>
        public bool UpdateExistingGames
        {
            get => updateExistingGames;
            set => SetValue(ref updateExistingGames, value);
        }

        /// <summary>When matching by name, only accept games that are on a Switch platform.</summary>
        public bool OnlyMatchSwitchPlatform
        {
            get => onlyMatchSwitchPlatform;
            set => SetValue(ref onlyMatchSwitchPlatform, value);
        }

        // ---- Playtime ----

        /// <summary>Write the Exophase playtime into Playnite.</summary>
        public bool ImportPlaytime
        {
            get => importPlaytime;
            set => SetValue(ref importPlaytime, value);
        }

        /// <summary>Overwrite an existing playtime with the Exophase value; otherwise keep the larger one.</summary>
        public bool OverwriteExistingPlaytime
        {
            get => overwriteExistingPlaytime;
            set => SetValue(ref overwriteExistingPlaytime, value);
        }

        // ---- Other data ----

        /// <summary>Also copy the last-played date from Exophase.</summary>
        public bool ImportLastActivity
        {
            get => importLastActivity;
            set => SetValue(ref importLastActivity, value);
        }

        // ---- Metadata ----

        /// <summary>Download metadata (cover, description, …) via IGDB for imported games.</summary>
        public bool DownloadMetadata
        {
            get => downloadMetadata;
            set => SetValue(ref downloadMetadata, value);
        }

        /// <summary>Replace metadata fields even when they are already set; otherwise only fill empty fields.</summary>
        public bool OverwriteMetadata
        {
            get => overwriteMetadata;
            set => SetValue(ref overwriteMetadata, value);
        }

        // ---- Filters ----

        /// <summary>Skip entries whose title looks like a demo.</summary>
        public bool SkipDemos
        {
            get => skipDemos;
            set => SetValue(ref skipDemos, value);
        }

        /// <summary>Ignore games played for less than this many minutes (0 = no filter).</summary>
        public int MinPlaytimeMinutes
        {
            get => minPlaytimeMinutes;
            set => SetValue(ref minPlaytimeMinutes, value);
        }

        // ---- Internal ----

        /// <summary>Name (substring) of the metadata plugin to use, e.g. "IGDB".</summary>
        public string MetadataProviderName { get; set; } = "IGDB";

        // Resolved playerProfileId cache, keyed by the username it was resolved from.
        public string CachedUsername { get; set; } = string.Empty;
        public string CachedPlayerId { get; set; } = string.Empty;
    }

    public class SwitchPlaytimeSettingsViewModel : ObservableObject, ISettings
    {
        private readonly SwitchPlaytimeExophasePlugin plugin;
        private SwitchPlaytimeSettings editingClone;
        private SwitchPlaytimeSettings settings;

        public SwitchPlaytimeSettings Settings
        {
            get => settings;
            set => SetValue(ref settings, value);
        }

        public SwitchPlaytimeSettingsViewModel(SwitchPlaytimeExophasePlugin plugin)
        {
            this.plugin = plugin;
            var saved = plugin.LoadPluginSettings<SwitchPlaytimeSettings>();
            Settings = saved ?? new SwitchPlaytimeSettings();
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            Settings = editingClone;
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(Settings.Username))
            {
                errors.Add("Enter your Exophase username.");
            }

            if (!Settings.ImportSwitch1 && !Settings.ImportSwitch2)
            {
                errors.Add("Enable at least one platform (Switch and/or Switch 2).");
            }

            if (!Settings.AddMissingGames && !Settings.UpdateExistingGames)
            {
                errors.Add("Enable at least one library action (add missing and/or update existing).");
            }

            if (Settings.MinPlaytimeMinutes < 0)
            {
                errors.Add("Minimum playtime can't be negative.");
            }

            return errors.Count == 0;
        }
    }
}
