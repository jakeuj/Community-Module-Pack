using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Events_Module {

    internal enum EventRewardLimit {
        None,
        AccountDaily,
        CharacterDaily
    }

    internal sealed class EventRewardSource {
        public string Name { get; internal set; }
        public string Url  { get; internal set; }
    }

    internal sealed class EventRewardSummary {
        public string   Id                         { get; internal set; }
        public int?     MinimumRareOrExoticItems   { get; internal set; }
        public EventRewardLimit RareOrExoticLimit  { get; internal set; }
        public string   DragoniteAmount            { get; internal set; }
        public string   CompactDragoniteAmount     { get; internal set; }
        public EventRewardLimit DragoniteLimit     { get; internal set; }
        public int?     GuaranteedCoinCopper       { get; internal set; }
        public EventRewardLimit CoinLimit          { get; internal set; }
        public string   NoteKey                     { get; internal set; }
        public IReadOnlyList<EventRewardSource> Sources { get; internal set; }
        public DateTime VerifiedOn                  { get; internal set; }
    }

    internal sealed class EventRewardDetailFormats {
        public string Title { get; set; }
        public string RareOrExoticFormat { get; set; }
        public string RareAccountDailyLimit { get; set; }
        public string RareCharacterDailyLimit { get; set; }
        public string DragoniteFormat { get; set; }
        public string DragoniteAccountDailyLimit { get; set; }
        public string DragoniteCharacterDailyLimit { get; set; }
        public string CoinFormat { get; set; }
        public string CoinAccountDailyLimit { get; set; }
        public string CoinCharacterDailyLimit { get; set; }
        public string SourceFormat { get; set; }
        public string SourceSeparator { get; set; }
        public string VerifiedFormat { get; set; }
        public Func<string, string> NoteResolver { get; set; }
    }

    internal sealed class EventRewardCatalog {

        [JsonObject]
        private sealed class CatalogDocument {
            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("verifiedOn")]
            public DateTime VerifiedOn { get; set; }

            [JsonProperty("events")]
            public List<RewardDefinition> Events { get; set; }
        }

        [JsonObject]
        private sealed class RewardSourceDefinition {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("url")]
            public string Url { get; set; }
        }

        [JsonObject]
        private sealed class RewardDefinition {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("names")]
            public List<string> Names { get; set; }

            [JsonProperty("stableIds")]
            public List<string> StableIds { get; set; }

            [JsonProperty("waypoint")]
            public string Waypoint { get; set; }

            [JsonProperty("minimumRareOrExoticItems")]
            public int? MinimumRareOrExoticItems { get; set; }

            [JsonProperty("rareOrExoticLimit")]
            public string RareOrExoticLimit { get; set; }

            [JsonProperty("dragonite")]
            public string DragoniteAmount { get; set; }

            [JsonProperty("compactDragonite")]
            public string CompactDragoniteAmount { get; set; }

            [JsonProperty("dragoniteLimit")]
            public string DragoniteLimit { get; set; }

            [JsonProperty("guaranteedCoinCopper")]
            public int? GuaranteedCoinCopper { get; set; }

            [JsonProperty("coinLimit")]
            public string CoinLimit { get; set; }

            [JsonProperty("verifiedOn")]
            public DateTime? VerifiedOn { get; set; }

            [JsonProperty("noteKey")]
            public string NoteKey { get; set; }

            [JsonProperty("sources")]
            public List<RewardSourceDefinition> Sources { get; set; }
        }

        private readonly CatalogDocument _document;
        private readonly Dictionary<string, RewardDefinition> _byStableId;
        private readonly Dictionary<string, RewardDefinition> _byWaypoint;
        private readonly Dictionary<string, RewardDefinition> _byName;

        public static EventRewardCatalog Empty { get; } = new EventRewardCatalog(new CatalogDocument {
            Version = 3,
            Events = new List<RewardDefinition>()
        });

        public int Count => _document.Events.Count;

        private EventRewardCatalog(CatalogDocument document) {
            _document = document;
            _byStableId = new Dictionary<string, RewardDefinition>(StringComparer.Ordinal);
            _byWaypoint = new Dictionary<string, RewardDefinition>(StringComparer.Ordinal);
            _byName = new Dictionary<string, RewardDefinition>(StringComparer.Ordinal);

            foreach (RewardDefinition definition in document.Events) {
                foreach (string stableId in definition.StableIds) {
                    _byStableId.Add(stableId.Trim(), definition);
                }

                if (!string.IsNullOrWhiteSpace(definition.Waypoint)) {
                    _byWaypoint.Add(definition.Waypoint.Trim(), definition);
                }

                foreach (string name in definition.Names) {
                    _byName.Add(Normalize(name), definition);
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

        public EventRewardSummary Match(string stableId,
                                        string waypoint,
                                        bool waypointIsUnique,
                                        params string[] names) {
            RewardDefinition definition = null;

            if (!string.IsNullOrWhiteSpace(stableId)) {
                _byStableId.TryGetValue(stableId.Trim(), out definition);
            }

            if (definition == null && waypointIsUnique && !string.IsNullOrWhiteSpace(waypoint) &&
                _byWaypoint.TryGetValue(waypoint.Trim(), out RewardDefinition waypointDefinition) &&
                NamesMatch(waypointDefinition, names)) {
                definition = waypointDefinition;
            }

            if (definition == null) {
                foreach (string name in names ?? Array.Empty<string>()) {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (_byName.TryGetValue(Normalize(name), out definition)) break;
                }
            }

            if (definition == null) return null;

            bool hasDragonite = !string.IsNullOrWhiteSpace(definition.DragoniteAmount);
            var sources = definition.Sources
                                    .Select(source => new EventRewardSource {
                                        Name = source.Name.Trim(),
                                        Url = source.Url.Trim()
                                    })
                                    .ToList()
                                    .AsReadOnly();

            return new EventRewardSummary {
                Id = definition.Id,
                MinimumRareOrExoticItems = definition.MinimumRareOrExoticItems,
                RareOrExoticLimit = ParseLimit(definition.RareOrExoticLimit),
                DragoniteAmount = definition.DragoniteAmount,
                CompactDragoniteAmount = !hasDragonite
                    ? null
                    : string.IsNullOrWhiteSpace(definition.CompactDragoniteAmount)
                    ? definition.DragoniteAmount
                    : definition.CompactDragoniteAmount,
                DragoniteLimit = ParseLimit(definition.DragoniteLimit),
                GuaranteedCoinCopper = definition.GuaranteedCoinCopper,
                CoinLimit = ParseLimit(definition.CoinLimit),
                NoteKey = definition.NoteKey,
                Sources = sources,
                VerifiedOn = (definition.VerifiedOn ?? _document.VerifiedOn).Date
            };
        }

        private static void Validate(CatalogDocument document) {
            if (document == null || document.Version != 3) {
                throw new InvalidDataException("The event reward catalog version is unsupported.");
            }

            if (!IsVerificationDateValid(document.VerifiedOn)) {
                throw new InvalidDataException("The event reward catalog has an invalid verification date.");
            }

            if (document.Events == null || document.Events.Count == 0) {
                throw new InvalidDataException("The event reward catalog has no events.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var stableIds = new HashSet<string>(StringComparer.Ordinal);
            var waypoints = new HashSet<string>(StringComparer.Ordinal);
            var aliases = new HashSet<string>(StringComparer.Ordinal);

            foreach (RewardDefinition definition in document.Events) {
                if (definition == null || string.IsNullOrWhiteSpace(definition.Id) || !ids.Add(definition.Id)) {
                    throw new InvalidDataException("The event reward catalog contains an invalid or duplicate event ID.");
                }

                if (definition.Names == null || definition.Names.Count == 0) {
                    throw new InvalidDataException($"Reward event '{definition.Id}' has no names.");
                }

                foreach (string name in definition.Names) {
                    string normalizedName = Normalize(name);
                    if (string.IsNullOrWhiteSpace(normalizedName) || !aliases.Add(normalizedName)) {
                        throw new InvalidDataException($"Reward event '{definition.Id}' has an invalid or duplicate name alias.");
                    }
                }

                if (definition.StableIds == null || definition.StableIds.Count == 0) {
                    throw new InvalidDataException($"Reward event '{definition.Id}' has no stable IDs.");
                }

                foreach (string stableId in definition.StableIds) {
                    string trimmedStableId = stableId?.Trim();
                    if (string.IsNullOrWhiteSpace(trimmedStableId) ||
                        !trimmedStableId.StartsWith("wiki:", StringComparison.Ordinal) ||
                        !stableIds.Add(trimmedStableId)) {
                        throw new InvalidDataException($"Reward event '{definition.Id}' has an invalid or duplicate stable ID.");
                    }
                }

                if (!string.IsNullOrWhiteSpace(definition.Waypoint) &&
                    !waypoints.Add(definition.Waypoint.Trim())) {
                    throw new InvalidDataException($"Reward event '{definition.Id}' has an invalid or duplicate waypoint.");
                }

                bool hasRareOrExotic = definition.MinimumRareOrExoticItems.HasValue;
                bool hasDragonite = !string.IsNullOrWhiteSpace(definition.DragoniteAmount);
                bool hasCoin = definition.GuaranteedCoinCopper.HasValue;

                if (hasRareOrExotic && definition.MinimumRareOrExoticItems.Value <= 0) {
                    throw new InvalidDataException($"Reward event '{definition.Id}' has an invalid guaranteed rare or exotic item count.");
                }

                ValidateLimitPair(definition.Id, "rare or exotic", hasRareOrExotic, definition.RareOrExoticLimit);

                if (!hasDragonite && !string.IsNullOrWhiteSpace(definition.CompactDragoniteAmount)) {
                    throw new InvalidDataException($"Reward event '{definition.Id}' has a compact Dragonite amount without a full amount.");
                }

                ValidateLimitPair(definition.Id, "Dragonite", hasDragonite, definition.DragoniteLimit);

                if (hasCoin && definition.GuaranteedCoinCopper.Value <= 0) {
                    throw new InvalidDataException($"Reward event '{definition.Id}' has an invalid guaranteed coin amount.");
                }

                ValidateLimitPair(definition.Id, "coin", hasCoin, definition.CoinLimit);

                if (!hasRareOrExotic && !hasDragonite && !hasCoin) {
                    throw new InvalidDataException($"Reward event '{definition.Id}' has no supported reward components.");
                }

                if (definition.VerifiedOn.HasValue && !IsVerificationDateValid(definition.VerifiedOn.Value)) {
                    throw new InvalidDataException($"Reward event '{definition.Id}' has an invalid verification date.");
                }

                ValidateSources(definition);
            }
        }

        private static void ValidateLimitPair(string id, string component, bool hasComponent, string limit) {
            EventRewardLimit parsedLimit = ParseLimit(limit);
            if (hasComponent && parsedLimit == EventRewardLimit.None) {
                throw new InvalidDataException($"Reward event '{id}' has no supported {component} limit.");
            }

            if (!hasComponent && !string.IsNullOrWhiteSpace(limit)) {
                throw new InvalidDataException($"Reward event '{id}' has a {component} limit without that reward component.");
            }
        }

        private static void ValidateSources(RewardDefinition definition) {
            if (definition.Sources == null || definition.Sources.Count == 0) {
                throw new InvalidDataException($"Reward event '{definition.Id}' has no sources.");
            }

            var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RewardSourceDefinition source in definition.Sources) {
                if (source == null || string.IsNullOrWhiteSpace(source.Name) || !IsWikiUrl(source.Url) ||
                    !urls.Add(source.Url.Trim())) {
                    throw new InvalidDataException($"Reward event '{definition.Id}' has an invalid or duplicate source.");
                }
            }
        }

        private static bool NamesMatch(RewardDefinition definition, IEnumerable<string> names) {
            if (definition == null) return false;

            var aliases = new HashSet<string>(definition.Names.Select(Normalize), StringComparer.Ordinal);
            return (names ?? Enumerable.Empty<string>())
                   .Where(name => !string.IsNullOrWhiteSpace(name))
                   .Select(Normalize)
                   .Any(aliases.Contains);
        }

        private static bool IsVerificationDateValid(DateTime value) {
            // Verification dates are author-local calendar dates. Allow the next UTC day so
            // the catalog stays valid worldwide while that date rolls across time zones.
            return value != default(DateTime) && value.Date <= DateTime.UtcNow.Date.AddDays(1);
        }

        private static EventRewardLimit ParseLimit(string value) {
            string normalized = value?.Trim();
            if (string.Equals(normalized, "account-daily", StringComparison.Ordinal)) {
                return EventRewardLimit.AccountDaily;
            }

            if (string.Equals(normalized, "character-daily", StringComparison.Ordinal)) {
                return EventRewardLimit.CharacterDaily;
            }

            return EventRewardLimit.None;
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

    internal static class EventRewardTextFormatter {

        internal static string FormatCoin(int copper) {
            if (copper <= 0) return string.Empty;

            int gold = copper / 10000;
            int silver = copper % 10000 / 100;
            int remainingCopper = copper % 100;
            var parts = new List<string>(3);

            if (gold > 0) parts.Add(gold.ToString(CultureInfo.InvariantCulture) + "G");
            if (silver > 0) parts.Add(silver.ToString(CultureInfo.InvariantCulture) + "S");
            if (remainingCopper > 0) parts.Add(remainingCopper.ToString(CultureInfo.InvariantCulture) + "C");

            return string.Join(" ", parts);
        }

        internal static string BuildCompactSummary(EventRewardSummary reward,
                                                   string prefix,
                                                   string separator,
                                                   string rareOrExoticFormat,
                                                   string dragoniteFormat,
                                                   string accountDailyCoinFormat) {
            if (reward == null) return string.Empty;

            var parts = new List<string>(3);

            if (reward.MinimumRareOrExoticItems.HasValue) {
                parts.Add(string.Format(rareOrExoticFormat ?? "{0}", reward.MinimumRareOrExoticItems.Value));
            }

            string dragoniteAmount = string.IsNullOrWhiteSpace(reward.CompactDragoniteAmount)
                ? reward.DragoniteAmount
                : reward.CompactDragoniteAmount;
            if (!string.IsNullOrWhiteSpace(dragoniteAmount)) {
                parts.Add(string.Format(dragoniteFormat ?? "{0}", dragoniteAmount));
            }

            if (reward.GuaranteedCoinCopper.HasValue) {
                string coin = FormatCoin(reward.GuaranteedCoinCopper.Value);
                if (!string.IsNullOrWhiteSpace(coin)) {
                    string format = reward.CoinLimit == EventRewardLimit.AccountDaily
                        ? accountDailyCoinFormat
                        : "{0}";
                    parts.Add(string.Format(format ?? "{0}", coin));
                }
            }

            return parts.Count == 0
                ? string.Empty
                : (prefix ?? string.Empty) + string.Join(separator ?? string.Empty, parts);
        }

        internal static string BuildDetailedSummary(EventRewardSummary reward, EventRewardDetailFormats formats) {
            if (reward == null || formats == null) return string.Empty;

            var message = new StringBuilder();
            message.AppendLine(formats.Title ?? string.Empty);
            bool hasSection = false;

            if (reward.MinimumRareOrExoticItems.HasValue) {
                AppendSectionSeparator(message, ref hasSection);
                message.AppendLine(string.Format(formats.RareOrExoticFormat ?? "{0}", reward.MinimumRareOrExoticItems.Value));
                AppendLimit(message, reward.RareOrExoticLimit,
                            formats.RareAccountDailyLimit, formats.RareCharacterDailyLimit);
            }

            if (!string.IsNullOrWhiteSpace(reward.DragoniteAmount)) {
                AppendSectionSeparator(message, ref hasSection);
                message.AppendLine(string.Format(formats.DragoniteFormat ?? "{0}", reward.DragoniteAmount));
                AppendLimit(message, reward.DragoniteLimit,
                            formats.DragoniteAccountDailyLimit, formats.DragoniteCharacterDailyLimit);
            }

            if (!string.IsNullOrWhiteSpace(reward.NoteKey)) {
                string note = formats.NoteResolver?.Invoke(reward.NoteKey);
                if (!string.IsNullOrWhiteSpace(note)) message.AppendLine(note);
            }

            if (reward.GuaranteedCoinCopper.HasValue) {
                AppendSectionSeparator(message, ref hasSection);
                message.AppendLine(string.Format(formats.CoinFormat ?? "{0}",
                                                 FormatCoin(reward.GuaranteedCoinCopper.Value)));
                AppendLimit(message, reward.CoinLimit,
                            formats.CoinAccountDailyLimit, formats.CoinCharacterDailyLimit);
            }

            IReadOnlyList<EventRewardSource> sources = reward.Sources ?? Array.Empty<EventRewardSource>();
            if (sources.Count > 0) {
                message.AppendLine();
                message.AppendLine(string.Format(
                    formats.SourceFormat ?? "{0}",
                    string.Join(formats.SourceSeparator ?? ", ", sources.Select(source => source.Name))
                ));
            }

            message.Append(string.Format(formats.VerifiedFormat ?? "{0:yyyy-MM-dd}", reward.VerifiedOn));
            return message.ToString();
        }

        private static void AppendSectionSeparator(StringBuilder message, ref bool hasSection) {
            if (hasSection) message.AppendLine();
            hasSection = true;
        }

        private static void AppendLimit(StringBuilder message,
                                        EventRewardLimit limit,
                                        string accountDaily,
                                        string characterDaily) {
            string text = limit == EventRewardLimit.AccountDaily
                ? accountDaily
                : limit == EventRewardLimit.CharacterDaily
                ? characterDaily
                : null;
            if (!string.IsNullOrWhiteSpace(text)) message.AppendLine(text);
        }
    }
}
