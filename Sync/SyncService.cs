using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Playnite.SDK;
using Playnite.SDK.Models;
using NintendoExophaseImporter.Exophase;
using NintendoExophaseImporter.Settings;

namespace NintendoExophaseImporter.Sync
{
    public class SyncResult
    {
        public int TotalSwitchGames { get; set; }
        public int Updated { get; set; }
        public int Imported { get; set; }
        public int SkippedAmbiguous { get; set; }
        public int MetadataApplied { get; set; }
        public bool MetadataAttempted { get; set; }

        public override string ToString()
        {
            var text = $"{TotalSwitchGames} Switch / Switch 2 game(s) found on Exophase.\n" +
                       $"• {Updated} updated\n" +
                       $"• {Imported} imported\n" +
                       $"• {SkippedAmbiguous} skipped (ambiguous name match)";
            if (MetadataAttempted)
            {
                text += $"\n• {MetadataApplied} metadata fetched (IGDB)";
            }
            return text;
        }
    }

    public class SyncService
    {
        private const string GameIdPrefix = "exophase:";
        private const string Switch1SpecId = "nintendo_switch";
        private const string Switch1PlatformName = "Nintendo Switch";
        private const string Switch2PlatformName = "Nintendo Switch 2";
        private const string SourceName = "Nintendo";

        private readonly IPlayniteAPI api;
        private readonly NintendoExophaseImporterPlugin plugin;
        private readonly ILogger logger;

        public SyncService(IPlayniteAPI api, NintendoExophaseImporterPlugin plugin, ILogger logger)
        {
            this.api = api;
            this.plugin = plugin;
            this.logger = logger;
        }

        public SyncResult Sync(Action<string> reportProgress, CancellationToken token)
        {
            var settings = plugin.SettingsViewModel.Settings;
            var client = new ExophaseClient(api, logger);

            // 1. Resolve the Exophase player id (cached per username).
            reportProgress?.Invoke("Identifying Exophase profile...");
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
                plugin.SavePluginSettings(settings);
            }

            // 2. Download Switch games from Exophase, then apply the platform and filter options.
            var allEntries = client.GetSwitchGames(playerId, reportProgress, token);
            ulong minSeconds = settings.MinPlaytimeMinutes > 0 ? (ulong)settings.MinPlaytimeMinutes * 60UL : 0UL;
            var entries = allEntries
                .Where(e => (e.IsSwitch1 && settings.ImportSwitch1) || (e.IsSwitch2 && settings.ImportSwitch2))
                .Where(e => !settings.SkipDemos || !LooksLikeDemo(e.Title))
                .Where(e => minSeconds == 0 || e.PlaytimeSeconds >= minSeconds)
                .ToList();

            var result = new SyncResult { TotalSwitchGames = entries.Count };
            if (entries.Count == 0)
            {
                return result;
            }

            // 3. Resolve (creating if needed) the Switch platforms and the Exophase source.
            Guid switch1Id = (settings.ImportSwitch1 && entries.Any(e => e.IsSwitch1))
                ? GetOrCreatePlatform(Switch1PlatformName, Switch1SpecId) : Guid.Empty;
            Guid switch2Id = (settings.ImportSwitch2 && entries.Any(e => e.IsSwitch2))
                ? GetOrCreatePlatform(Switch2PlatformName, null) : Guid.Empty;
            var switchPlatformIds = new HashSet<Guid>(new[] { switch1Id, switch2Id }.Where(g => g != Guid.Empty));

            var source = api.Database.Sources.FirstOrDefault(s =>
                string.Equals(s.Name, SourceName, StringComparison.OrdinalIgnoreCase));
            if (source == null)
            {
                source = new GameSource(SourceName);
                api.Database.Sources.Add(source);
            }

            // 4. Build lookup indexes over the existing library.
            var allGames = api.Database.Games.ToList();
            var importedByKey = allGames
                .Where(g => !string.IsNullOrEmpty(g.GameId) && g.GameId.StartsWith(GameIdPrefix, StringComparison.Ordinal))
                .GroupBy(g => g.GameId)
                .ToDictionary(grp => grp.Key, grp => grp.First());

            var byName = new Dictionary<string, List<Game>>();
            foreach (var g in allGames)
            {
                var key = NameMatcher.Normalize(g.Name);
                if (key.Length == 0)
                {
                    continue;
                }
                if (!byName.TryGetValue(key, out var list))
                {
                    list = new List<Game>();
                    byName[key] = list;
                }
                list.Add(g);
            }

            // 5. Apply.
            var metadataTargets = new List<Game>();
            int index = 0;
            using (api.Database.BufferedUpdate())
            {
                foreach (var entry in entries)
                {
                    token.ThrowIfCancellationRequested();
                    reportProgress?.Invoke($"Processing {++index}/{entries.Count}: {entry.Title}");

                    if (string.IsNullOrWhiteSpace(entry.Title))
                    {
                        continue;
                    }

                    Game target;
                    bool isOurImport = importedByKey.TryGetValue(entry.Key, out target);
                    if (!isOurImport)
                    {
                        target = FindNameMatch(entry, byName, switchPlatformIds, settings.OnlyMatchSwitchPlatform, out var ambiguous);
                        if (ambiguous)
                        {
                            result.SkippedAmbiguous++;
                            logger.Info($"Ambiguous match for '{entry.Title}', skipped.");
                            continue;
                        }
                    }

                    if (target != null)
                    {
                        // Our own previous imports are always kept in sync; other existing games
                        // only when "update existing" is enabled.
                        if (!isOurImport && !settings.UpdateExistingGames)
                        {
                            continue;
                        }

                        if (ApplyUpdate(target, entry, settings))
                        {
                            api.Database.Games.Update(target);
                        }
                        result.Updated++;

                        // Queue our imports for (re)metadata: when overwriting, or when still missing a cover.
                        if (isOurImport && (settings.OverwriteMetadata || string.IsNullOrEmpty(target.CoverImage)))
                        {
                            metadataTargets.Add(target);
                        }
                    }
                    else if (settings.AddMissingGames)
                    {
                        var platformIds = PlatformIdsFor(entry, settings.ImportSwitch1, settings.ImportSwitch2, switch1Id, switch2Id);
                        var game = CreateGame(entry, platformIds, source.Id, settings);
                        api.Database.Games.Add(game);
                        metadataTargets.Add(game);
                        result.Imported++;
                    }
                }
            }

            // 6. Metadata phase (IGDB) for imported games, outside the buffered update so
            //    covers/descriptions appear progressively.
            if (settings.DownloadMetadata && metadataTargets.Count > 0)
            {
                result.MetadataAttempted = true;
                var metaService = new MetadataService(api, logger);
                var provider = metaService.FindProvider(settings.MetadataProviderName ?? "IGDB");
                if (provider == null)
                {
                    logger.Warn("No metadata plugin found; skipping metadata download.");
                }
                else
                {
                    logger.Info($"Using metadata provider '{provider.Name}' for {metadataTargets.Count} game(s).");
                    int mi = 0;
                    foreach (var g in metadataTargets)
                    {
                        token.ThrowIfCancellationRequested();
                        reportProgress?.Invoke($"Metadata {++mi}/{metadataTargets.Count}: {g.Name}");
                        if (metaService.Apply(provider, g, settings.OverwriteMetadata, token))
                        {
                            api.Database.Games.Update(g);
                            result.MetadataApplied++;
                        }
                    }
                }
            }

            logger.Info("Exophase Switch sync finished: " + result);
            return result;
        }

        private static List<Guid> PlatformIdsFor(SwitchGameEntry entry, bool importSwitch1, bool importSwitch2, Guid switch1Id, Guid switch2Id)
        {
            var ids = new List<Guid>();
            if (entry.IsSwitch1 && importSwitch1 && switch1Id != Guid.Empty)
            {
                ids.Add(switch1Id);
            }
            if (entry.IsSwitch2 && importSwitch2 && switch2Id != Guid.Empty)
            {
                ids.Add(switch2Id);
            }
            return ids;
        }

        private Guid GetOrCreatePlatform(string name, string specId)
        {
            var existing = api.Database.Platforms.FirstOrDefault(p =>
                (specId != null && string.Equals(p.SpecificationId, specId, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                return existing.Id;
            }

            var platform = new Platform(name);
            if (specId != null)
            {
                platform.SpecificationId = specId;
            }
            api.Database.Platforms.Add(platform);
            logger.Info($"Created platform '{name}'.");
            return platform.Id;
        }

        private Game FindNameMatch(
            SwitchGameEntry entry,
            Dictionary<string, List<Game>> byName,
            HashSet<Guid> switchPlatformIds,
            bool onlyMatchSwitchPlatform,
            out bool ambiguous)
        {
            ambiguous = false;
            var key = NameMatcher.Normalize(entry.Title);
            if (key.Length == 0 || !byName.TryGetValue(key, out var candidates) || candidates.Count == 0)
            {
                return null;
            }

            // Prefer games already on a Switch platform.
            var switchCandidates = candidates.Where(g => IsSwitchGame(g, switchPlatformIds)).ToList();
            if (switchCandidates.Count == 1)
            {
                return switchCandidates[0];
            }
            if (switchCandidates.Count > 1)
            {
                ambiguous = true;
                return null;
            }

            // No Switch-platform candidate.
            if (onlyMatchSwitchPlatform)
            {
                return null;
            }

            // Otherwise accept a single non-Switch name match.
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            ambiguous = true;
            return null;
        }

        private static bool LooksLikeDemo(string title)
        {
            return !string.IsNullOrEmpty(title)
                && Regex.IsMatch(title, @"\bdemo\b", RegexOptions.IgnoreCase);
        }

        private bool IsSwitchGame(Game game, HashSet<Guid> switchPlatformIds)
        {
            if (game.PlatformIds != null && switchPlatformIds.Count > 0 && game.PlatformIds.Any(switchPlatformIds.Contains))
            {
                return true;
            }

            var platforms = game.Platforms;
            return platforms != null && platforms.Any(p =>
                !string.IsNullOrEmpty(p.Name) &&
                p.Name.IndexOf("switch", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <returns>true if anything changed (so the caller persists the game).</returns>
        private bool ApplyUpdate(Game game, SwitchGameEntry entry, NintendoExophaseSettings settings)
        {
            bool changed = false;

            // Use a real value only, to avoid wiping local data on a transient 0.
            if (settings.ImportPlaytime && entry.PlaytimeSeconds > 0)
            {
                bool shouldSet = settings.OverwriteExistingPlaytime
                    ? game.Playtime != entry.PlaytimeSeconds   // overwrite with Exophase value
                    : entry.PlaytimeSeconds > game.Playtime;   // keep the larger value
                if (shouldSet)
                {
                    game.Playtime = entry.PlaytimeSeconds;
                    changed = true;
                }
            }

            if (settings.ImportLastActivity && entry.LastPlayed.HasValue && game.LastActivity != entry.LastPlayed)
            {
                game.LastActivity = entry.LastPlayed;
                changed = true;
            }

            return changed;
        }

        private Game CreateGame(SwitchGameEntry entry, List<Guid> platformIds, Guid sourceId, NintendoExophaseSettings settings)
        {
            var game = new Game(entry.Title)
            {
                GameId = entry.Key,
                SourceId = sourceId,
                Playtime = settings.ImportPlaytime ? entry.PlaytimeSeconds : 0UL,
                Added = DateTime.Now,
                IsInstalled = false
            };

            if (platformIds != null && platformIds.Count > 0)
            {
                game.PlatformIds = platformIds;
            }

            if (settings.ImportLastActivity && entry.LastPlayed.HasValue)
            {
                game.LastActivity = entry.LastPlayed;
            }

            if (!string.IsNullOrWhiteSpace(entry.Url))
            {
                game.Links = new ObservableCollection<Link> { new Link("Exophase", entry.Url) };
            }

            return game;
        }
    }
}
