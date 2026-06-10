using System.Collections.Generic;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace NintendoExophaseImporter.Settings
{
    public class NintendoExophaseSettings : ObservableObject
    {
        private string username = string.Empty;
        private bool importSwitch1 = true;
        private bool importSwitch2 = true;
        private bool importPlaytime = true;
        private bool importLastActivity = true;
        private bool skipDemos = false;
        private int minPlaytimeMinutes = 0;

        public string Username
        {
            get => username;
            set => SetValue(ref username, value);
        }

        public bool ImportSwitch1
        {
            get => importSwitch1;
            set => SetValue(ref importSwitch1, value);
        }

        public bool ImportSwitch2
        {
            get => importSwitch2;
            set => SetValue(ref importSwitch2, value);
        }

        public bool ImportPlaytime
        {
            get => importPlaytime;
            set => SetValue(ref importPlaytime, value);
        }

        public bool ImportLastActivity
        {
            get => importLastActivity;
            set => SetValue(ref importLastActivity, value);
        }

        public bool SkipDemos
        {
            get => skipDemos;
            set => SetValue(ref skipDemos, value);
        }

        public int MinPlaytimeMinutes
        {
            get => minPlaytimeMinutes;
            set => SetValue(ref minPlaytimeMinutes, value);
        }

        // Internal cache
        public string CachedUsername { get; set; } = string.Empty;
        public string CachedPlayerId { get; set; } = string.Empty;
    }

    public class NintendoExophaseSettingsViewModel : ObservableObject, ISettings
    {
        private readonly NintendoExophaseImporterPlugin plugin;
        private NintendoExophaseSettings editingClone;
        private NintendoExophaseSettings settings;

        public NintendoExophaseSettings Settings
        {
            get => settings;
            set => SetValue(ref settings, value);
        }

        public NintendoExophaseSettingsViewModel(NintendoExophaseImporterPlugin plugin)
        {
            this.plugin = plugin;
            var saved = plugin.LoadPluginSettings<NintendoExophaseSettings>();
            Settings = saved ?? new NintendoExophaseSettings();
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
                errors.Add("Enter your Exophase username.");

            if (!Settings.ImportSwitch1 && !Settings.ImportSwitch2)
                errors.Add("Enable at least one platform (Switch and/or Switch 2).");

            if (Settings.MinPlaytimeMinutes < 0)
                errors.Add("Minimum playtime can't be negative.");

            return errors.Count == 0;
        }
    }
}
