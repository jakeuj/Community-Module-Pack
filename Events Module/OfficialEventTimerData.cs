using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Events_Module {

    internal static class OfficialEventTimerEndpoint {
        public const string ApiEndpoint = "https://wiki.guildwars2.com/api.php";
        public const string UserAgent = "BlishHUD-EventsModule/1.0.9 (https://github.com/blish-hud/Community-Module-Pack)";
        public const int RequestTimeoutSeconds = 15;
    }

    internal sealed class OfficialEventTimerData {
        [JsonProperty("config")]
        public OfficialEventTimerConfig Config { get; set; }

        [JsonProperty("events")]
        public Dictionary<string, OfficialEventGroup> Events { get; set; }
    }

    internal sealed class OfficialEventTimerConfig {
        [JsonProperty("version")]
        public string Version { get; set; }
    }

    internal sealed class OfficialEventGroup {
        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("link")]
        public string Link { get; set; }

        [JsonProperty("segments")]
        public Dictionary<string, OfficialEventSegment> Segments { get; set; }

        [JsonProperty("sequences")]
        public OfficialEventSequences Sequences { get; set; }
    }

    internal sealed class OfficialEventSegment {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("link")]
        public string Link { get; set; }

        [JsonProperty("chatlink")]
        public string ChatLink { get; set; }
    }

    internal sealed class OfficialEventSequences {
        [JsonProperty("partial")]
        public List<OfficialEventSequenceItem> Partial { get; set; }

        [JsonProperty("pattern")]
        public List<OfficialEventSequenceItem> Pattern { get; set; }
    }

    internal sealed class OfficialEventSequenceItem {
        [JsonProperty("r")]
        public int Reference { get; set; }

        [JsonProperty("d")]
        public int Duration { get; set; }
    }

    internal sealed class OfficialEventDefinition {
        public string StableId       { get; set; }
        public string GroupId        { get; set; }
        public string SegmentId      { get; set; }
        public string Name           { get; set; }
        public string GroupName      { get; set; }
        public string Category       { get; set; }
        public string Wiki           { get; set; }
        public string Waypoint       { get; set; }
        public int    Duration       { get; set; }
        public List<int> StartMinutesUtc { get; set; } = new List<int>();
    }

    internal sealed class OfficialEventTimerParseResult {
        public string Version { get; set; }
        public List<OfficialEventDefinition> Events { get; set; }
    }

    internal static class OfficialEventTimerParser {

        private const int MinutesPerDay = 24 * 60;

        // These seasonal groups are intentionally omitted by the official Event timers page.
        private static readonly HashSet<string> ExcludedGroups = new HashSet<string>(StringComparer.Ordinal) {
            "t",
            "festival-lc",
            "festival-ha",
            "festival-db"
        };

        public static OfficialEventTimerParseResult Parse(string json, bool requireProductionCounts = true) {
            if (string.IsNullOrWhiteSpace(json)) {
                throw new InvalidDataException("The official event timer payload is empty.");
            }

            OfficialEventTimerData data;
            try {
                data = JsonConvert.DeserializeObject<OfficialEventTimerData>(json);
            } catch (JsonException exception) {
                throw new InvalidDataException("The official event timer payload is not valid JSON.", exception);
            }

            if (data?.Config == null || string.IsNullOrWhiteSpace(data.Config.Version)) {
                throw new InvalidDataException("The official event timer payload has no version.");
            }

            if (data.Events == null || data.Events.Count == 0) {
                throw new InvalidDataException("The official event timer payload has no event groups.");
            }

            if (requireProductionCounts && (data.Events.Count < 10 || data.Events.Count > 200)) {
                throw new InvalidDataException($"Unexpected official event group count: {data.Events.Count}.");
            }

            var definitions = new List<OfficialEventDefinition>();

            foreach (var groupPair in data.Events) {
                ValidateGroup(groupPair.Key, groupPair.Value);

                if (ExcludedGroups.Contains(groupPair.Key)) continue;

                definitions.AddRange(ExpandGroup(groupPair.Key, groupPair.Value));
            }

            if (requireProductionCounts && (definitions.Count < 20 || definitions.Count > 500)) {
                throw new InvalidDataException($"Unexpected official event definition count: {definitions.Count}.");
            }

            if (definitions.Select(definition => definition.StableId).Distinct(StringComparer.Ordinal).Count() != definitions.Count) {
                throw new InvalidDataException("The official event timer payload contains duplicate event identifiers.");
            }

            return new OfficialEventTimerParseResult {
                Version = data.Config.Version.Trim(),
                Events = definitions
            };
        }

        private static void ValidateGroup(string groupId, OfficialEventGroup group) {
            if (string.IsNullOrWhiteSpace(groupId) || group == null) {
                throw new InvalidDataException("The official event timer payload contains an invalid group.");
            }

            if (group.Segments == null || group.Segments.Count == 0 || group.Sequences?.Partial == null || group.Sequences.Pattern == null) {
                throw new InvalidDataException($"Official event group '{groupId}' is incomplete.");
            }

            ValidateSequence(groupId, group, group.Sequences.Partial);
            ValidateSequence(groupId, group, group.Sequences.Pattern);

            int partialDuration = group.Sequences.Partial.Sum(item => item.Duration);
            int patternDuration = group.Sequences.Pattern.Sum(item => item.Duration);
            if (partialDuration < MinutesPerDay && patternDuration <= 0) {
                throw new InvalidDataException($"Official event group '{groupId}' cannot fill a complete day.");
            }

            foreach (var segmentPair in group.Segments) {
                string chatLink = segmentPair.Value?.ChatLink?.Trim();
                if (!string.IsNullOrEmpty(chatLink) && !IsValidWaypointChatLink(chatLink)) {
                    throw new InvalidDataException($"Official event group '{groupId}' contains an invalid waypoint chat link.");
                }
            }
        }

        private static void ValidateSequence(string groupId, OfficialEventGroup group, IEnumerable<OfficialEventSequenceItem> sequence) {
            foreach (var item in sequence) {
                if (item == null || item.Duration <= 0 || item.Duration > MinutesPerDay) {
                    throw new InvalidDataException($"Official event group '{groupId}' contains an invalid duration.");
                }

                string segmentId = item.Reference.ToString(CultureInfo.InvariantCulture);
                if (!group.Segments.ContainsKey(segmentId)) {
                    throw new InvalidDataException($"Official event group '{groupId}' references missing segment '{segmentId}'.");
                }
            }
        }

        private static IEnumerable<OfficialEventDefinition> ExpandGroup(string groupId, OfficialEventGroup group) {
            var startsBySegment = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var durationBySegment = new Dictionary<string, int>(StringComparer.Ordinal);
            int minute = 0;

            AppendSequence(group, group.Sequences.Partial, startsBySegment, durationBySegment, ref minute);

            while (minute < MinutesPerDay) {
                int beforePattern = minute;
                AppendSequence(group, group.Sequences.Pattern, startsBySegment, durationBySegment, ref minute);
                if (minute <= beforePattern) {
                    throw new InvalidDataException($"Official event group '{groupId}' has a non-advancing sequence.");
                }
            }

            foreach (var segmentPair in group.Segments) {
                if (string.IsNullOrWhiteSpace(segmentPair.Value?.Name)) continue;
                if (!startsBySegment.TryGetValue(segmentPair.Key, out List<int> starts) || starts.Count == 0) continue;

                string chatLink = segmentPair.Value.ChatLink?.Trim() ?? string.Empty;
                yield return new OfficialEventDefinition {
                    StableId = $"wiki:{groupId}:{segmentPair.Key}",
                    GroupId = groupId,
                    SegmentId = segmentPair.Key,
                    Name = segmentPair.Value.Name.Trim(),
                    GroupName = group.Name?.Trim() ?? string.Empty,
                    Category = group.Category?.Trim() ?? string.Empty,
                    Wiki = BuildWikiUrl(segmentPair.Value.Link ?? group.Link),
                    Waypoint = chatLink,
                    Duration = durationBySegment.TryGetValue(segmentPair.Key, out int duration) ? duration : 0,
                    StartMinutesUtc = starts.Distinct().OrderBy(value => value).ToList()
                };
            }
        }

        private static void AppendSequence(OfficialEventGroup group,
                                           IEnumerable<OfficialEventSequenceItem> sequence,
                                           IDictionary<string, List<int>> startsBySegment,
                                           IDictionary<string, int> durationBySegment,
                                           ref int minute) {
            foreach (var item in sequence) {
                string segmentId = item.Reference.ToString(CultureInfo.InvariantCulture);
                OfficialEventSegment segment = group.Segments[segmentId];

                if (minute < MinutesPerDay && !string.IsNullOrWhiteSpace(segment.Name)) {
                    if (!startsBySegment.TryGetValue(segmentId, out List<int> starts)) {
                        starts = new List<int>();
                        startsBySegment.Add(segmentId, starts);
                    }

                    starts.Add(minute);
                    if (!durationBySegment.ContainsKey(segmentId)) durationBySegment.Add(segmentId, item.Duration);
                }

                minute += item.Duration;
            }
        }

        internal static bool IsValidWaypointChatLink(string chatLink) {
            if (string.IsNullOrWhiteSpace(chatLink)) return false;

            string trimmed = chatLink.Trim();
            if (!trimmed.StartsWith("[&", StringComparison.Ordinal) || !trimmed.EndsWith("]", StringComparison.Ordinal)) return false;

            string encoded = trimmed.Substring(2, trimmed.Length - 3);
            try {
                byte[] decoded = Convert.FromBase64String(encoded);
                return decoded.Length >= 5 && decoded[0] == 0x04;
            } catch (FormatException) {
                return false;
            }
        }

        private static string BuildWikiUrl(string link) {
            if (string.IsNullOrWhiteSpace(link)) return string.Empty;

            string trimmed = link.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri absolute)) {
                return absolute.Scheme == Uri.UriSchemeHttps &&
                       string.Equals(absolute.Host, "wiki.guildwars2.com", StringComparison.OrdinalIgnoreCase)
                    ? absolute.AbsoluteUri
                    : string.Empty;
            }

            string[] titleParts = trimmed.Split(new[] { '#' }, 2);
            string title = Uri.EscapeDataString(titleParts[0].Replace(' ', '_')).Replace("%2F", "/");
            string anchor = titleParts.Length == 2
                ? "#" + Uri.EscapeDataString(titleParts[1].Replace(' ', '_'))
                : string.Empty;

            return "https://wiki.guildwars2.com/wiki/" + title + anchor;
        }
    }
}
