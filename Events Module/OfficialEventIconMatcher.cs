using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Events_Module {

    internal sealed class OfficialEventIconCandidate {
        public string Name       { get; set; }
        public string Colloquial { get; set; }
        public string Category   { get; set; }
        public string Location   { get; set; }
        public string Waypoint   { get; set; }
        public string Icon       { get; set; }
    }

    internal static class OfficialEventIconMatcher {

        private const int MinimumMatchScore = 60;

        private static readonly IReadOnlyDictionary<string, string> LocalIconPaths =
            new Dictionary<string, string>(StringComparer.Ordinal) {
                ["wiki:core-dn:1"] = @"textures\events\day.png",
                ["wiki:core-dn:2"] = @"textures\events\dusk.png",
                ["wiki:core-dn:3"] = @"textures\events\night.png",
                ["wiki:core-dn:4"] = @"textures\events\dawn.png",
                ["wiki:eod-dn:1"] = @"textures\events\day.png",
                ["wiki:eod-dn:2"] = @"textures\events\dusk.png",
                ["wiki:eod-dn:3"] = @"textures\events\night.png",
                ["wiki:eod-dn:4"] = @"textures\events\dawn.png",
                ["wiki:voe-dn:1"] = @"textures\events\day.png",
                ["wiki:voe-dn:2"] = @"textures\events\dusk.png",
                ["wiki:voe-dn:3"] = @"textures\events\night.png",
                ["wiki:voe-dn:4"] = @"textures\events\dawn.png",
                ["wiki:core-ateu:1"] = @"textures\events\tournament-balthazar.png",
                ["wiki:core-ateu:2"] = @"textures\events\tournament-grenth.png",
                ["wiki:core-ateu:3"] = @"textures\events\tournament-melandru.png",
                ["wiki:core-ateu:4"] = @"textures\events\tournament-lyssa.png",
                ["wiki:core-atna:1"] = @"textures\events\tournament-balthazar.png",
                ["wiki:core-atna:2"] = @"textures\events\tournament-grenth.png",
                ["wiki:core-atna:3"] = @"textures\events\tournament-melandru.png",
                ["wiki:core-atna:4"] = @"textures\events\tournament-lyssa.png",
                ["wiki:core-in:1"] = @"textures\events\invasion-awakened.png",
                ["wiki:core-in:2"] = @"textures\events\invasion-scarlet.png",
                ["wiki:voe-eg:1"] = @"textures\events\shackles-of-the-ancients.png"
            };

        public static string FindLocalIconPath(string stableId) {
            if (string.IsNullOrWhiteSpace(stableId)) return null;

            return LocalIconPaths.TryGetValue(stableId, out string path) ? path : null;
        }

        public static string FindBestIcon(OfficialEventDefinition definition,
                                          IEnumerable<OfficialEventIconCandidate> candidates) {
            if (definition == null || candidates == null) return null;

            OfficialEventIconCandidate bestMatch = null;
            int bestScore = 0;

            foreach (OfficialEventIconCandidate candidate in candidates.Where(item => !string.IsNullOrWhiteSpace(item?.Icon))) {
                int score = Score(definition, candidate);
                if (score <= bestScore) continue;

                bestScore = score;
                bestMatch = candidate;
            }

            return bestScore >= MinimumMatchScore ? bestMatch?.Icon : null;
        }

        private static int Score(OfficialEventDefinition definition, OfficialEventIconCandidate candidate) {
            string officialName = Normalize(definition.Name);
            string candidateName = Normalize(candidate.Name);
            string colloquial = Normalize(candidate.Colloquial);
            string groupName = Normalize(definition.GroupName);
            string location = Normalize(candidate.Location);
            int score = 0;

            if (!string.IsNullOrWhiteSpace(definition.Waypoint) &&
                string.Equals(definition.Waypoint.Trim(), candidate.Waypoint?.Trim(), StringComparison.Ordinal)) {
                score = Math.Max(score, 110);
            }

            if (officialName.Length > 0 && candidateName == officialName) score = Math.Max(score, 100);
            if (officialName.Length > 0 && colloquial == officialName) score = Math.Max(score, 95);

            // The official timer uses shortened labels such as "Golem Mark II" and
            // "Noran's Homestead" while the bundled data includes descriptive prefixes.
            if (officialName.Length >= 8 &&
                (candidateName.EndsWith(" " + officialName, StringComparison.Ordinal) ||
                 candidateName.StartsWith(officialName + " ", StringComparison.Ordinal) ||
                 officialName.EndsWith(" " + candidateName, StringComparison.Ordinal) ||
                 officialName.StartsWith(candidateName + " ", StringComparison.Ordinal))) {
                score = Math.Max(score, 85);
            }

            // Phases without a dedicated icon inherit the icon used for their map/meta group.
            if (groupName.Length > 0 && location == groupName) score = Math.Max(score, 60);

            if (Normalize(candidate.Category) == Normalize(definition.Category)) score = Math.Max(score, 30);

            return score;
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
