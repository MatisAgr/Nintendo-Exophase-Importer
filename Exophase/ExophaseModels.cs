using System.Collections.Generic;
using Playnite.SDK.Data;

namespace SwitchPlaytimeExophase.Exophase
{
    /// <summary>
    /// Top level response of https://api.exophase.com/public/player/{id}/games
    /// </summary>
    public class ExophaseGamesResponse
    {
        [SerializationPropertyName("success")]
        public bool Success { get; set; }

        [SerializationPropertyName("games")]
        public List<ExophaseGame> Games { get; set; }
    }

    public class ExophaseGame
    {
        [SerializationPropertyName("master_id")]
        public long Id { get; set; }

        /// <summary>Human-readable playtime such as "117h 55m" or "7m" (can be empty).
        /// We prefer <see cref="PlaytimeUnits"/> and only parse this as a fallback.</summary>
        [SerializationPropertyName("playtime")]
        public string Playtime { get; set; }

        [SerializationPropertyName("playtimeUnits")]
        public ExophasePlaytimeUnits PlaytimeUnits { get; set; }

        [SerializationPropertyName("lastplayed_utc")]
        public long LastPlayedUtc { get; set; }

        [SerializationPropertyName("percent")]
        public double Percent { get; set; }

        [SerializationPropertyName("meta")]
        public ExophaseMeta Meta { get; set; }
    }

    public class ExophasePlaytimeUnits
    {
        [SerializationPropertyName("hours")]
        public int? Hours { get; set; }

        [SerializationPropertyName("minutes")]
        public int? Minutes { get; set; }
    }

    public class ExophaseMeta
    {
        [SerializationPropertyName("title")]
        public string Title { get; set; }

        [SerializationPropertyName("platforms")]
        public List<ExophasePlatform> Platforms { get; set; }

        [SerializationPropertyName("canonical_url")]
        public string CanonicalUrl { get; set; }

        [SerializationPropertyName("image")]
        public string Image { get; set; }
    }

    public class ExophasePlatform
    {
        [SerializationPropertyName("name")]
        public string Name { get; set; }

        [SerializationPropertyName("slug")]
        public string Slug { get; set; }
    }
}
