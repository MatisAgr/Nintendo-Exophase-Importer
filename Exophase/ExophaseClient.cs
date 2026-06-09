using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace SwitchPlaytimeExophase.Exophase
{
    /// <summary>
    /// One Nintendo Switch game as projected from Exophase, ready to be matched
    /// against / imported into the Playnite database.
    /// </summary>
    public class SwitchGameEntry
    {
        public long ExophaseId { get; set; }
        public string Title { get; set; }
        public ulong PlaytimeSeconds { get; set; }
        public DateTime? LastPlayed { get; set; }
        public string Url { get; set; }

        /// <summary>Game is listed on the original Nintendo Switch.</summary>
        public bool IsSwitch1 { get; set; }

        /// <summary>Game is listed on Nintendo Switch 2.</summary>
        public bool IsSwitch2 { get; set; }

        /// <summary>Stable key stored in Game.GameId so re-syncs update instead of duplicating.</summary>
        public string Key => "exophase:" + (ExophaseId > 0 ? ExophaseId.ToString() : (Title ?? string.Empty).ToLowerInvariant());
    }

    public class ExophaseClient
    {
        private const string ProfileUrlFormat = "https://www.exophase.com/user/{0}/";
        private const string GamesApiFormat =
            "https://api.exophase.com/public/player/{0}/games?page={1}&environment=&sort=1&showHidden=0";
        private const int MaxPages = 200;

        // www.exophase.com is behind Cloudflare bot protection, so a plain HttpClient
        // gets a 403/challenge. We load those pages through Playnite's embedded Chromium
        // (CefSharp) instead, which solves the challenge like a real browser.
        private static readonly HttpClient http;

        private readonly IPlayniteAPI api;
        private readonly ILogger logger;

        static ExophaseClient()
        {
            // net462 does not negotiate TLS 1.2 by default; api.exophase.com requires it.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.exophase.com/");
        }

        public ExophaseClient(IPlayniteAPI api, ILogger logger)
        {
            this.api = api;
            this.logger = logger;
        }

        /// <summary>
        /// Resolves the playerProfileId from a profile URL or username by reading the
        /// "window.playerProfileId = ..." value embedded in the page HTML (loaded via the
        /// embedded browser to get past Cloudflare).
        /// </summary>
        public string ResolvePlayerId(string profileUrlOrUsername)
        {
            var url = BuildProfileUrl(profileUrlOrUsername);
            logger.Info($"Resolving Exophase playerProfileId from {url}");

            string html = FetchViaBrowser(url, isJson: false);
            if (string.IsNullOrEmpty(html))
            {
                throw new ExophaseException(
                    $"Could not load the Exophase profile page ({url}). " +
                    "Check the username and make sure the profile is public.");
            }

            var match = Regex.Match(html, @"playerProfileId\s*[=:]\s*['""]?([A-Za-z0-9_]+)");
            if (!match.Success)
            {
                throw new ExophaseException(
                    "Could not find 'playerProfileId' on the profile page. " +
                    "Make sure the username is correct and the profile is public.");
            }

            var id = match.Groups[1].Value;
            logger.Info($"Resolved Exophase playerProfileId = {id}");
            return id;
        }

        /// <summary>
        /// Downloads every game on the profile (all platforms, paginated) and keeps only Switch entries.
        /// </summary>
        public List<SwitchGameEntry> GetSwitchGames(string playerId, Action<string> reportProgress, CancellationToken token)
        {
            var result = new List<SwitchGameEntry>();
            bool useBrowserFallback = false;

            for (int page = 1; page <= MaxPages; page++)
            {
                token.ThrowIfCancellationRequested();
                reportProgress?.Invoke($"Reading Exophase games (page {page})...");

                var url = string.Format(GamesApiFormat, Uri.EscapeDataString(playerId), page);

                string json = null;
                if (!useBrowserFallback)
                {
                    json = TryHttpGet(url, out var status);
                    if (json == null)
                    {
                        logger.Warn($"HttpClient failed for {url} (status {status}); falling back to embedded browser.");
                        useBrowserFallback = true;
                    }
                }

                if (useBrowserFallback)
                {
                    var source = FetchViaBrowser(url, isJson: true);
                    json = ExtractJson(source);
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    throw new ExophaseException(
                        "Exophase returned no data. Is the username correct and the profile public?");
                }

                ExophaseGamesResponse response;
                try
                {
                    response = Serialization.FromJson<ExophaseGamesResponse>(json);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Failed to parse Exophase games response.");
                    throw new ExophaseException("Could not read the Exophase response (unexpected format).", ex);
                }

                if (response == null || !response.Success || response.Games == null || response.Games.Count == 0)
                {
                    break;
                }

                foreach (var game in response.Games)
                {
                    GetSwitchFlags(game, out var isSwitch1, out var isSwitch2);
                    if (!isSwitch1 && !isSwitch2)
                    {
                        continue;
                    }

                    result.Add(new SwitchGameEntry
                    {
                        ExophaseId = game.Id,
                        Title = game.Meta?.Title?.Trim(),
                        PlaytimeSeconds = ComputePlaytimeSeconds(game),
                        LastPlayed = game.LastPlayedUtc > 0
                            ? DateTimeOffset.FromUnixTimeSeconds(game.LastPlayedUtc).UtcDateTime
                            : (DateTime?)null,
                        Url = game.Meta?.CanonicalUrl,
                        IsSwitch1 = isSwitch1,
                        IsSwitch2 = isSwitch2
                    });
                }

                Thread.Sleep(useBrowserFallback ? 100 : 250);
            }

            logger.Info($"Found {result.Count} Nintendo Switch / Switch 2 game(s) on Exophase profile.");
            return result;
        }

        /// <summary>Returns body on HTTP 2xx, otherwise null (never throws).</summary>
        private string TryHttpGet(string url, out int statusCode)
        {
            statusCode = 0;
            try
            {
                using (var resp = http.GetAsync(url).GetAwaiter().GetResult())
                {
                    statusCode = (int)resp.StatusCode;
                    var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    return resp.IsSuccessStatusCode ? body : null;
                }
            }
            catch (Exception ex)
            {
                logger.Warn($"HttpClient request to {url} failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Loads a URL in Playnite's offscreen Chromium view and returns the page source,
        /// waiting for any Cloudflare challenge to resolve. Must be called off the UI thread.
        /// </summary>
        private string FetchViaBrowser(string url, bool isJson)
        {
            try
            {
                using (var view = api.WebViews.CreateOffscreenView())
                {
                    view.NavigateAndWait(url);

                    string source = view.GetPageSource();
                    // Give Cloudflare's JS challenge time to complete and redirect to the real page.
                    for (int i = 0; i < 20 && LooksLikeChallenge(source); i++)
                    {
                        Thread.Sleep(1000);
                        source = view.GetPageSource();
                    }

                    if (LooksLikeChallenge(source))
                    {
                        logger.Warn($"Cloudflare challenge did not resolve for {url}.");
                    }

                    return source;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Embedded browser failed to load {url}");
                throw new ExophaseException($"The embedded browser could not load {url}: {ex.Message}", ex);
            }
        }

        private static bool LooksLikeChallenge(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return true;
            }
            return source.IndexOf("Just a moment", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("cf-browser-verification", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("Checking your browser", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("__cf_chl", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Extracts the JSON object from a browser-rendered JSON page (HTML-wrapped).</summary>
        private static string ExtractJson(string pageSource)
        {
            if (string.IsNullOrEmpty(pageSource))
            {
                return null;
            }

            int start = pageSource.IndexOf('{');
            int end = pageSource.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return null;
            }

            var json = pageSource.Substring(start, end - start + 1);
            return WebUtility.HtmlDecode(json);
        }

        private static void GetSwitchFlags(ExophaseGame game, out bool isSwitch1, out bool isSwitch2)
        {
            isSwitch1 = false;
            isSwitch2 = false;
            var platforms = game?.Meta?.Platforms;
            if (platforms == null)
            {
                return;
            }

            foreach (var p in platforms)
            {
                if (p == null)
                {
                    continue;
                }

                // Prefer the slug ("switch" / "switch-2"); fall back to the display name.
                if (string.Equals(p.Slug, "switch-2", StringComparison.OrdinalIgnoreCase) ||
                    (p.Name != null && p.Name.IndexOf("switch 2", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    isSwitch2 = true;
                }
                else if (string.Equals(p.Slug, "switch", StringComparison.OrdinalIgnoreCase) ||
                         (p.Name != null && p.Name.IndexOf("switch", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    isSwitch1 = true;
                }
            }
        }

        private static ulong ComputePlaytimeSeconds(ExophaseGame game)
        {
            var units = game.PlaytimeUnits;
            if (units != null && (units.Hours.HasValue || units.Minutes.HasValue))
            {
                long seconds = (long)(units.Hours ?? 0) * 3600L + (long)(units.Minutes ?? 0) * 60L;
                return seconds > 0 ? (ulong)seconds : 0UL;
            }

            // Fallback: parse the human-readable string such as "117h 55m" / "7m" / "24h".
            return ParsePlaytimeString(game.Playtime);
        }

        private static ulong ParsePlaytimeString(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0UL;
            }

            long seconds = 0;
            var h = Regex.Match(text, @"(\d+)\s*h");
            if (h.Success)
            {
                seconds += long.Parse(h.Groups[1].Value) * 3600L;
            }
            var m = Regex.Match(text, @"(\d+)\s*m");
            if (m.Success)
            {
                seconds += long.Parse(m.Groups[1].Value) * 60L;
            }
            return seconds > 0 ? (ulong)seconds : 0UL;
        }

        private static string BuildProfileUrl(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                throw new ExophaseException("No Exophase username provided.");
            }

            input = input.Trim();

            if (input.IndexOf("exophase.com", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!input.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    input = "https://" + input;
                }
                return input;
            }

            // Treat as a bare username.
            return string.Format(ProfileUrlFormat, Uri.EscapeDataString(input));
        }
    }

    public class ExophaseException : Exception
    {
        public ExophaseException(string message) : base(message) { }
        public ExophaseException(string message, Exception inner) : base(message, inner) { }
    }
}
