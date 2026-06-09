using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace NintendoExophaseImporter.Sync
{
    /// <summary>
    /// Fills in game metadata (cover, description, genres, release date, …) by driving an
    /// installed metadata plugin (IGDB by default) directly, the same way Playnite does
    /// during a metadata download.
    /// </summary>
    public class MetadataService
    {
        private readonly IPlayniteAPI api;
        private readonly ILogger logger;
        private readonly HttpClient http;

        public MetadataService(IPlayniteAPI api, ILogger logger)
        {
            this.api = api;
            this.logger = logger;
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        /// <summary>Finds an installed metadata plugin whose name contains <paramref name="preferred"/> (e.g. "IGDB").</summary>
        public MetadataPlugin FindProvider(string preferred)
        {
            var metadataPlugins = api.Addons.Plugins.OfType<MetadataPlugin>().ToList();
            if (metadataPlugins.Count == 0)
            {
                return null;
            }

            return metadataPlugins.FirstOrDefault(p =>
                       !string.IsNullOrEmpty(p.Name) &&
                       p.Name.IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? metadataPlugins.First();
        }

        /// <summary>
        /// Strips a "Demo" marker so the IGDB search resolves to the parent game.
        /// "STREET FIGHTER 6 DEMO" -> "STREET FIGHTER 6", "Game (Demo)" -> "Game".
        /// Returns the original name if there is no demo marker (or nothing would remain).
        /// </summary>
        private static string DemoSearchName(string name)
        {
            if (string.IsNullOrEmpty(name) || !Regex.IsMatch(name, @"\bdemo\b", RegexOptions.IgnoreCase))
            {
                return name;
            }

            var cleaned = Regex.Replace(name, @"\s*[\(\[]?\s*\bdemo\b\s*[\)\]]?\s*", " ", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim().TrimEnd('-', ':', '_', ' ');
            return string.IsNullOrEmpty(cleaned) ? name : cleaned;
        }

        /// <returns>true if at least one field was applied.</returns>
        public bool Apply(MetadataPlugin plugin, Game game, bool overwrite, CancellationToken token)
        {
            OnDemandMetadataProvider provider = null;
            try
            {
                // For demo entries, search IGDB by the base title (without "Demo") so we still get
                // the parent game's cover/description, while the entry keeps its real name.
                var searchName = DemoSearchName(game.Name);
                var requestData = string.Equals(searchName, game.Name, StringComparison.Ordinal)
                    ? game
                    : new Game(searchName) { PlatformIds = game.PlatformIds };

                provider = plugin.GetMetadataProvider(new MetadataRequestOptions(requestData, true));
                if (provider == null)
                {
                    return false;
                }

                var args = new GetMetadataFieldArgs();

                // Confirm the provider actually matched a game; otherwise skip to avoid wrong data.
                var name = SafeGet(() => provider.GetName(args));
                if (string.IsNullOrWhiteSpace(name))
                {
                    logger.Info($"No metadata match for '{game.Name}'.");
                    return false;
                }

                bool applied = false;

                applied |= SetText(game.Description, v => game.Description = v, SafeGet(() => provider.GetDescription(args)), overwrite);

                var release = SafeGet(() => provider.GetReleaseDate(args));
                if (release.HasValue && (overwrite || game.ReleaseDate == null))
                {
                    game.ReleaseDate = release;
                    applied = true;
                }

                applied |= ApplyList(SafeGet(() => provider.GetGenres(args)), api.Database.Genres, ids => game.GenreIds = ids, game.GenreIds, overwrite);
                applied |= ApplyList(SafeGet(() => provider.GetDevelopers(args)), api.Database.Companies, ids => game.DeveloperIds = ids, game.DeveloperIds, overwrite);
                applied |= ApplyList(SafeGet(() => provider.GetPublishers(args)), api.Database.Companies, ids => game.PublisherIds = ids, game.PublisherIds, overwrite);
                applied |= ApplyList(SafeGet(() => provider.GetFeatures(args)), api.Database.Features, ids => game.FeatureIds = ids, game.FeatureIds, overwrite);

                var community = SafeGet(() => provider.GetCommunityScore(args));
                if (community.HasValue && (overwrite || game.CommunityScore == null)) { game.CommunityScore = community; applied = true; }
                var critic = SafeGet(() => provider.GetCriticScore(args));
                if (critic.HasValue && (overwrite || game.CriticScore == null)) { game.CriticScore = critic; applied = true; }

                var links = SafeGet(() => provider.GetLinks(args));
                if (links != null)
                {
                    if (game.Links == null)
                    {
                        game.Links = new System.Collections.ObjectModel.ObservableCollection<Link>();
                    }
                    foreach (var link in links)
                    {
                        if (link != null && game.Links.All(l => l.Url != link.Url))
                        {
                            game.Links.Add(link);
                            applied = true;
                        }
                    }
                }

                if (overwrite || string.IsNullOrEmpty(game.CoverImage))
                {
                    var cover = SaveImage(SafeGet(() => provider.GetCoverImage(args)), game.Id);
                    if (cover != null) { game.CoverImage = cover; applied = true; }
                }
                if (overwrite || string.IsNullOrEmpty(game.BackgroundImage))
                {
                    var bg = SaveImage(SafeGet(() => provider.GetBackgroundImage(args)), game.Id);
                    if (bg != null) { game.BackgroundImage = bg; applied = true; }
                }
                if (overwrite || string.IsNullOrEmpty(game.Icon))
                {
                    var icon = SaveImage(SafeGet(() => provider.GetIcon(args)), game.Id);
                    if (icon != null) { game.Icon = icon; applied = true; }
                }

                return applied;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Metadata download failed for '{game.Name}'.");
                return false;
            }
            finally
            {
                provider?.Dispose();
            }
        }

        private bool ApplyList<T>(
            IEnumerable<MetadataProperty> properties,
            IItemCollection<T> collection,
            Action<List<Guid>> assign,
            List<Guid> existing,
            bool overwrite) where T : DatabaseObject
        {
            if (properties == null || (!overwrite && existing != null && existing.Count > 0))
            {
                return false;
            }

            var items = collection.Add(properties.ToList());
            var ids = items.Select(i => i.Id).ToList();
            if (ids.Count == 0)
            {
                return false;
            }

            assign(ids);
            return true;
        }

        private static bool SetText(string current, Action<string> set, string value, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            if (overwrite || string.IsNullOrEmpty(current))
            {
                set(value);
                return true;
            }
            return false;
        }

        private string SaveImage(MetadataFile file, Guid gameId)
        {
            if (file == null)
            {
                return null;
            }

            try
            {
                if (file.HasContent)
                {
                    return AddFromBytes(file.Content, file.FileName, gameId);
                }

                if (!string.IsNullOrEmpty(file.Path))
                {
                    if (file.Path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        var bytes = http.GetByteArrayAsync(file.Path).GetAwaiter().GetResult();
                        return AddFromBytes(bytes, file.FileName ?? Path.GetFileName(new Uri(file.Path).LocalPath), gameId);
                    }

                    if (File.Exists(file.Path))
                    {
                        return api.Database.AddFile(file.Path, gameId);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn($"Could not save image for game {gameId}: {ex.Message}");
            }

            return null;
        }

        private string AddFromBytes(byte[] bytes, string fileName, Guid gameId)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            var safeName = string.IsNullOrWhiteSpace(fileName) ? Guid.NewGuid().ToString("N") + ".jpg" : Path.GetFileName(fileName);
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "_" + safeName);
            File.WriteAllBytes(temp, bytes);
            try
            {
                return api.Database.AddFile(temp, gameId);
            }
            finally
            {
                try { File.Delete(temp); } catch { /* best effort */ }
            }
        }

        private T SafeGet<T>(Func<T> getter)
        {
            try
            {
                return getter();
            }
            catch (Exception ex)
            {
                logger.Warn($"Metadata field fetch failed: {ex.Message}");
                return default(T);
            }
        }
    }
}
