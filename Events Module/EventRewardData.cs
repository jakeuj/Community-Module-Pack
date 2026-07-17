using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Events_Module {

    internal sealed class EventRewardSummary {
        public string   Id                         { get; internal set; }
        public int      MinimumRareOrExoticItems   { get; internal set; }
        public string   DragoniteAmount            { get; internal set; }
        public string   CompactDragoniteAmount     { get; internal set; }
        public string   NoteKey                     { get; internal set; }
        public string   SourceName                  { get; internal set; }
        public string   SourceUrl                   { get; internal set; }
        public string   RulesSourceUrl              { get; internal set; }
        public DateTime VerifiedOn                  { get; internal set; }
    }

    internal sealed class EventRewardCatalog {

        [JsonObject]
        private sealed class CatalogDocument {
            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("verifiedOn")]
            public DateTime VerifiedOn { get; set; }

            [JsonProperty("minimumRareOrExoticItems")]
            public int MinimumRareOrExoticItems { get; set; }

            [JsonProperty("rulesSource")]
            public string RulesSourceUrl { get; set; }

            [JsonProperty("events")]
            public List<RewardDefinition> Events { get; set; }
        }

        [JsonObject]
        private sealed class RewardDefinition {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("names")]
            public List<string> Names { get; set; }

            [JsonProperty("waypoint")]
            public string Waypoint { get; set; }

            [JsonProperty("dragonite")]
            public string DragoniteAmount { get; set; }

            [JsonProperty("compactDragonite")]
            public string CompactDragoniteAmount { get; set; }

            [JsonProperty("noteKey")]
            public string NoteKey { get; set; }

            [JsonProperty("sourceName")]
            public string SourceName { get; set; }

            [JsonProperty("source")]
            public string SourceUrl { get; set; }
        }

        private readonly CatalogDocument _document;
        private readonly Dictionary<string, RewardDefinition> _byWaypoint;
        private readonly Dictionary<string, RewardDefinition> _byName;

        public static EventRewardCatalog Empty { get; } = new EventRewardCatalog(new CatalogDocument {
            Version = 1,
            Events = new List<RewardDefinition>()
        });

        public int Count => _document.Events.Count;

        private EventRewardCatalog(CatalogDocument document) {
            _document = document;
            _byWaypoint = new Dictionary<string, RewardDefinition>(StringComparer.Ordinal);
            _byName = new Dictionary<string, RewardDefinition>(StringComparer.Ordinal);

            foreach (RewardDefinition definition in document.Events) {
                _byWaypoint.Add(definition.Waypoint.Trim(), definition);

                foreach (string name in definition.Names) {
                    string normalized = Normalize(name);
                    if (!_byName.ContainsKey(normalized)) _byName.Add(normalized, definition);
                }
            }
        }

        public static EventRewardCatalog Parse(string json) {
            if (string.IsNullOrWhiteSpace(json)) {
                throw new InvalidDataException("The event reward catalog is empty.");
            }

            CatalogDocument document;
            try {
                document = JsonConvert.DeserializeObject<CatalogDocument>(json);
            } catch (JsonException exception) {
                throw new InvalidDataException("The event reward catalog is not valid JSON.", exception);
            }

            Validate(document);
            return new EventRewardCatalog(document);
        }

        public EventRewardSummary Match(string waypoint, params string[] names) {
            RewardDefinition definition = null;

            if (!string.IsNullOrWhiteSpace(waypoint)) {
                _byWaypoint.TryGetValue(waypoint.Trim(), out definition);
            }

            if (definition == null) {
                foreach (string name in names ?? Array.Empty<string>()) {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (_byName.TryGetValue(Normalize(name), out definition)) break;
                }
            }

            if (definition == null) return null;

            return new EventRewardSummary {
                Id = definition.Id,
                MinimumRareOrExoticItems = _document.MinimumRareOrExoticItems,
                DragoniteAmount = definition.DragoniteAmount,
                CompactDragoniteAmount = string.IsNullOrWhiteSpace(definition.CompactDragoniteAmount)
                    ? definition.DragoniteAmount
                    : definition.CompactDragoniteAmount,
                NoteKey = definition.NoteKey,
                SourceName = definition.SourceName,
                SourceUrl = definition.SourceUrl,
                RulesSourceUrl = _document.RulesSourceUrl,
                VerifiedOn = _document.VerifiedOn.Date
            };
        }

        private static void Validate(CatalogDocument document) {
            if (document == null || document.Version != 1) {
                throw new InvalidDataException("The event reward catalog version is unsupported.");
            }

            // Verification dates are author-local calendar dates. Allow the next UTC day so
            // the catalog stays valid worldwide while that date rolls across time zones.
            if (document.VerifiedOn == default(DateTime) ||
                document.VerifiedOn.Date > DateTime.UtcNow.Date.AddDays(1)) {
                throw new InvalidDataException("The event reward catalog has an invalid verification date.");
            }

            if (document.MinimumRareOrExoticItems <= 0) {
                throw new InvalidDataException("The event reward catalog has no guaranteed rare or exotic item count.");
            }

            if (!IsWikiUrl(document.RulesSourceUrl)) {
                throw new InvalidDataException("The event reward catalog rules source is invalid.");
            }

            if (document.Events == null || document.Events.Count == 0) {
                throw new InvalidDataException("The event reward catalog has no events.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var waypoints = new HashSet<string>(StringComparer.Ordinal);

            foreach (RewardDefinition definition in document.Events) {
                if (definition == null || string.IsNullOrWhiteSpace(definition.Id) || !ids.Add(definition.Id)) {
                    throw new InvalidDataException("The event reward catalog contains an invalid or duplicate event ID.");
                }

                if (definition.Names == null || definition.Names.All(string.IsNullOrWhiteSpace)) {
                    throw new InvalidDataException($"Reward event '{definition.Id}' has no names.");
                }

                if (string.IsNullOrWhiteSpace(definition.Waypoint) || !waypoints.Add(definition.Waypoint.Trim())) {
                    throw new InvalidDataException($"Reward event '{definition.Id}' has an invalid or duplicate waypoint.");
                }

                if (string.IsNullOrWhiteSpace(definition.DragoniteAmount)) {
                    throw new InvalidDataException($"Reward event '{definition.Id}' has no Dragonite Ore amount.");
                }

                if (string.IsNullOrWhiteSpace(definition.SourceName) || !IsWikiUrl(definition.SourceUrl)) {
                    throw new InvalidDataException($"Reward event '{definition.Id}' has an invalid source.");
                }
            }
        }

        private static bool IsWikiUrl(string source) {
            return Uri.TryCreate(source, UriKind.Absolute, out Uri uri) &&
                   uri.Scheme == Uri.UriSchemeHttps &&
                   string.Equals(uri.Host, "wiki.guildwars2.com", StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string value) {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var normalized = new StringBuilder(value.Length);
            bool pendingSpace = false;

            foreach (char character in value.Trim().ToLowerInvariant()) {
                if (char.IsLetterOrDigit(character)) {
                    if (pendingSpace && normalized.Length > 0) normalized.Append(' ');
                    normalized.Append(character);
                    pendingSpace = false;
                } else {
                    pendingSpace = normalized.Length > 0;
                }
            }

            return normalized.ToString();
        }
    }
}
