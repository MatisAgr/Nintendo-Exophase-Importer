using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using NintendoExophaseImporter.Exophase;
using NintendoExophaseImporter.Settings;

namespace NintendoExophaseImporter
{
    public class NintendoExophaseImporterPlugin : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public override Guid Id { get; } = Guid.Parse("1f9cc97c-a416-4b2e-b28d-a00fe5048404");
        public override string Name => "Nintendo Exophase Importer";

        public NintendoExophaseSettingsViewModel SettingsViewModel { get; }

        public NintendoExophaseImporterPlugin(IPlayniteAPI api) : base(api)
        {
            SettingsViewModel = new NintendoExophaseSettingsViewModel(this);
            Properties = new LibraryPluginProperties { HasSettings = true };
        }

        public override ISettings GetSettings(bool firstRunSettings) => SettingsViewModel;
        public override UserControl GetSettingsView(bool firstRunSettings) => new NintendoExophaseSettingsView();

        /// <summary>
        /// Called by Playnite when the user updates their library (Update Library button).
        /// Returns all Switch games from Exophase so Playnite can create/update DB entries.
        /// </summary>
        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var settings = SettingsViewModel.Settings;

            if (string.IsNullOrWhiteSpace(settings.Username))
            {
                logger.Warn("No Exophase username configured — skipping library update.");
                yield break;
            }

            var client = new ExophaseClient(PlayniteApi, logger);
            string playerId;

            if (string.Equals(settings.CachedUsername, settings.Username, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(settings.CachedPlayerId))
            {
                playerId = settings.CachedPlayerId;
            }
            else
            {
                playerId = client.ResolvePlayerId(settings.Username);
                settings.CachedUsername = settings.Username;
                settings.CachedPlayerId = playerId;
                SavePluginSettings(settings);
            }

            ulong minSeconds = settings.MinPlaytimeMinutes > 0 ? (ulong)settings.MinPlaytimeMinutes * 60UL : 0UL;
            var allEntries = client.GetSwitchGames(playerId, null, args.CancelToken);

            foreach (var entry in allEntries)
            {
                args.CancelToken.ThrowIfCancellationRequested();

                if (!((entry.IsSwitch1 && settings.ImportSwitch1) || (entry.IsSwitch2 && settings.ImportSwitch2)))
                    continue;
                if (settings.SkipDemos && IsDemo(entry.Title))
                    continue;
                if (minSeconds > 0 && entry.PlaytimeSeconds < minSeconds)
                    continue;
                if (string.IsNullOrWhiteSpace(entry.Title))
                    continue;

                var platforms = new HashSet<MetadataProperty>();
                if (entry.IsSwitch1 && settings.ImportSwitch1)
                    platforms.Add(new MetadataNameProperty("Nintendo Switch"));
                if (entry.IsSwitch2 && settings.ImportSwitch2)
                    platforms.Add(new MetadataNameProperty("Nintendo Switch 2"));

                var metadata = new GameMetadata
                {
                    Name = entry.Title,
                    GameId = entry.Key,
                    Playtime = settings.ImportPlaytime ? entry.PlaytimeSeconds : 0UL,
                    IsInstalled = false,
                    Platforms = platforms,
                    Source = new MetadataNameProperty("Nintendo"),
                };

                if (settings.ImportLastActivity && entry.LastPlayed.HasValue)
                    metadata.LastActivity = entry.LastPlayed;

                if (!string.IsNullOrWhiteSpace(entry.Url))
                    metadata.Links = new List<Link> { new Link("Exophase", entry.Url) };

                yield return metadata;
            }
        }

        private static bool IsDemo(string title)
        {
            return !string.IsNullOrEmpty(title)
                && Regex.IsMatch(title, @"\bdemo\b", RegexOptions.IgnoreCase);
        }
    }
}
