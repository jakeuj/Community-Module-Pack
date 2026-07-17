using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace Events_Module {
    internal static class Program {

        private const string Fixture = @"{
  'config': { 'version': 'test-v1' },
  'events': {
    'test-group': {
      'category': 'Test',
      'name': 'Test Group',
      'link': 'Test Group',
      'segments': {
        '0': { 'name': '' },
        '1': { 'name': 'Alpha', 'link': 'Alpha Event', 'chatlink': ' [&BEwCAAA=]' },
        '2': { 'name': 'Beta' }
      },
      'sequences': {
        'partial': [ { 'r': 0, 'd': 60 } ],
        'pattern': [
          { 'r': 1, 'd': 15 },
          { 'r': 0, 'd': 45 },
          { 'r': 2, 'd': 30 },
          { 'r': 0, 'd': 30 }
        ]
      }
    }
  }
}";

        private const string PartialOnlyFixture = @"{
  'config': { 'version': 'test-partial' },
  'events': {
    'partial-only': {
      'category': 'Test',
      'name': 'Partial Only',
      'segments': { '1': { 'name': 'Once' } },
      'sequences': {
        'partial': [ { 'r': 1, 'd': 1440 } ],
        'pattern': []
      }
    }
  }
}";

        public static int Main(string[] args) {
            try {
                RunOfflineTests();
                if (args.Any(argument => string.Equals(argument, "--live", StringComparison.OrdinalIgnoreCase))) {
                    RunLiveAudit();
                }

                Console.WriteLine("Official event timer parser tests passed.");
                return 0;
            } catch (Exception exception) {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void RunOfflineTests() {
            OfficialEventTimerParseResult parsed = OfficialEventTimerParser.Parse(Fixture, requireProductionCounts: false);
            Assert(parsed.Version == "test-v1", "Fixture version was not preserved.");
            Assert(parsed.Events.Count == 2, "Fixture should produce two named events.");

            OfficialEventDefinition alpha = parsed.Events.Single(item => item.StableId == "wiki:test-group:1");
            OfficialEventDefinition beta = parsed.Events.Single(item => item.StableId == "wiki:test-group:2");
            Assert(alpha.StartMinutesUtc.First() == 60, "Partial sequence was not applied before the repeating pattern.");
            Assert(beta.StartMinutesUtc.First() == 120, "Pattern offsets were expanded incorrectly.");
            Assert(alpha.Waypoint == "[&BEwCAAA=]", "Waypoint chat links should be trimmed.");
            Assert(alpha.Wiki == "https://wiki.guildwars2.com/wiki/Alpha_Event", "Wiki title was not converted to an official HTTPS URL.");

            OfficialEventTimerParseResult partialOnly = OfficialEventTimerParser.Parse(PartialOnlyFixture, requireProductionCounts: false);
            Assert(partialOnly.Events.Single().StartMinutesUtc.SequenceEqual(new[] { 0 }), "A complete partial sequence should not require a pattern.");

            AssertThrows(Fixture.Replace("'r': 2", "'r': 9"), "Missing segment references must be rejected.");
            AssertThrows(Fixture.Replace("[&BEwCAAA=]", "not-a-chat-link"), "Malformed waypoint chat links must be rejected.");

            RunIconMatcherTests();

            bool productionGuardRejectedFixture = false;
            try {
                OfficialEventTimerParser.Parse(Fixture);
            } catch {
                productionGuardRejectedFixture = true;
            }
            Assert(productionGuardRejectedFixture, "Production sanity-count checks did not reject a tiny payload.");
        }

        private static void RunIconMatcherTests() {
            var candidates = new[] {
                new OfficialEventIconCandidate {
                    Name = "Inquest Golem Mark II",
                    Location = "Mount Maelstrom",
                    Waypoint = "[&BNQCAAA=]",
                    Icon = "golem-icon"
                },
                new OfficialEventIconCandidate {
                    Name = "Battle For Lion's Arch (Public)",
                    Location = "Eye of the North",
                    Icon = "battle-icon"
                },
                new OfficialEventIconCandidate {
                    Name = "White Mantle Control: Noran's Homestead",
                    Location = "Lake Doric",
                    Icon = "noran-icon"
                },
                new OfficialEventIconCandidate {
                    Name = "Chak Gerent",
                    Location = "Tangled Depths",
                    Icon = "tangled-depths-icon"
                },
                new OfficialEventIconCandidate {
                    Name = "Ley-Line Anomaly (Timberline Falls)",
                    Location = "Timberline Falls",
                    Icon = "ley-line-icon"
                },
                new OfficialEventIconCandidate {
                    Name = "Tequatl the Sunless",
                    Location = "Sparkfly Fen",
                    Waypoint = "[&BNABAAA=]",
                    Icon = "tequatl-icon"
                },
                new OfficialEventIconCandidate {
                    Name = "No usable icon",
                    Location = "Unmatched",
                    Icon = null
                }
            };

            Assert(OfficialEventIconMatcher.FindBestIcon(new OfficialEventDefinition {
                Name = "Golem Mark II",
                GroupName = "World bosses"
            }, candidates) == "golem-icon", "Short official names should reuse descriptive bundled event icons.");

            Assert(OfficialEventIconMatcher.FindBestIcon(new OfficialEventDefinition {
                Name = "Battle For Lion's Arch",
                GroupName = "Eye of the North"
            }, candidates) == "battle-icon", "Parenthetical bundled suffixes should not prevent an icon match.");

            Assert(OfficialEventIconMatcher.FindBestIcon(new OfficialEventDefinition {
                Name = "Noran's Homestead",
                GroupName = "Lake Doric"
            }, candidates) == "noran-icon", "Descriptive bundled prefixes should not prevent an icon match.");

            Assert(OfficialEventIconMatcher.FindBestIcon(new OfficialEventDefinition {
                Name = "Prep",
                GroupName = "Tangled Depths"
            }, candidates) == "tangled-depths-icon", "Map phases should inherit their group icon.");

            Assert(OfficialEventIconMatcher.FindBestIcon(new OfficialEventDefinition {
                Name = "Timberline Falls",
                GroupName = "Ley-Line Anomaly"
            }, candidates) == "ley-line-icon", "Locations in bundled parentheses should remain available for icon matching.");

            Assert(OfficialEventIconMatcher.FindBestIcon(new OfficialEventDefinition {
                Name = "Localized or renamed Tequatl",
                GroupName = "World bosses",
                Waypoint = "[&BNABAAA=]"
            }, candidates) == "tequatl-icon", "Waypoint identity should preserve a boss icon when its display name changes.");

            Assert(OfficialEventIconMatcher.FindBestIcon(new OfficialEventDefinition {
                Name = "Automated Tournament: Melandru's Matchup",
                GroupName = "EU PvP Tournaments"
            }, candidates) == null, "Unrelated events should use the local fallback instead of a misleading icon.");

            var localIconMappings = new Dictionary<string, string[]> {
                [@"textures\events\day.png"] = new[] {
                    "wiki:core-dn:1", "wiki:eod-dn:1", "wiki:voe-dn:1"
                },
                [@"textures\events\dusk.png"] = new[] {
                    "wiki:core-dn:2", "wiki:eod-dn:2", "wiki:voe-dn:2"
                },
                [@"textures\events\night.png"] = new[] {
                    "wiki:core-dn:3", "wiki:eod-dn:3", "wiki:voe-dn:3"
                },
                [@"textures\events\dawn.png"] = new[] {
                    "wiki:core-dn:4", "wiki:eod-dn:4", "wiki:voe-dn:4"
                },
                [@"textures\events\tournament-balthazar.png"] = new[] {
                    "wiki:core-ateu:1", "wiki:core-atna:1"
                },
                [@"textures\events\tournament-grenth.png"] = new[] {
                    "wiki:core-ateu:2", "wiki:core-atna:2"
                },
                [@"textures\events\tournament-melandru.png"] = new[] {
                    "wiki:core-ateu:3", "wiki:core-atna:3"
                },
                [@"textures\events\tournament-lyssa.png"] = new[] {
                    "wiki:core-ateu:4", "wiki:core-atna:4"
                },
                [@"textures\events\invasion-awakened.png"] = new[] { "wiki:core-in:1" },
                [@"textures\events\invasion-scarlet.png"] = new[] { "wiki:core-in:2" },
                [@"textures\events\shackles-of-the-ancients.png"] = new[] { "wiki:voe-eg:1" }
            };
            int mappedEventCount = 0;

            foreach (KeyValuePair<string, string[]> mapping in localIconMappings) {
                foreach (string stableId in mapping.Value) {
                    Assert(OfficialEventIconMatcher.FindLocalIconPath(stableId) == mapping.Key,
                           "Stable event ID " + stableId + " did not map to " + mapping.Key + ".");
                    mappedEventCount++;
                }
            }

            Assert(mappedEventCount == 23, "Expected all 23 previously missing event icons to be mapped.");
            Assert(OfficialEventIconMatcher.FindLocalIconPath("wiki:future-event:1") == null,
                   "Unknown future events must continue to use the safe fallback icon.");
            Assert(OfficialEventIconMatcher.FindLocalIconPath(null) == null,
                   "Missing stable IDs must continue to use the safe fallback icon.");
        }

        private static void RunLiveAudit() {
            string endpoint = OfficialEventTimerEndpoint.ApiEndpoint + "?action=query&prop=revisions&titles=Widget%3AEvent_timer%2Fdata.json&rvprop=ids%7Ctimestamp%7Csha1%7Ccontent&rvslots=main&format=json&formatversion=2&maxlag=5";
            string response;

            using (var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
            using (var client = new HttpClient(handler)) {
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", OfficialEventTimerEndpoint.UserAgent);
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
                using (HttpResponseMessage httpResponse = client.GetAsync(endpoint).GetAwaiter().GetResult()) {
                    response = httpResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (!httpResponse.IsSuccessStatusCode) {
                        throw new InvalidOperationException($"Live HTTP audit failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}\n{response}");
                    }
                }
            }

            JObject envelope = JObject.Parse(response);
            string content = (string)envelope["query"]?["pages"]?[0]?["revisions"]?[0]?["slots"]?["main"]?["content"];
            OfficialEventTimerParseResult parsed = OfficialEventTimerParser.Parse(content);
            var byId = parsed.Events.ToDictionary(item => item.StableId, StringComparer.Ordinal);

            var expectedWaypoints = new Dictionary<string, string>(StringComparer.Ordinal) {
                ["wiki:core-la:1"] = "[&BEwCAAA=]",
                ["wiki:core-la:2"] = "[&BOcBAAA=]",
                ["wiki:core-la:3"] = "[&BOQAAAA=]",
                ["wiki:hot-vb:3"] = "[&BAgIAAA=]",
                ["wiki:pof-dv:2"] = "[&BO0KAAA=]",
                ["wiki:lws5-gv:1"] = "[&BA4MAAA=]"
            };

            foreach (var expected in expectedWaypoints) {
                Assert(byId.TryGetValue(expected.Key, out OfficialEventDefinition definition), "Missing live official event " + expected.Key + ".");
                Assert(definition.Waypoint == expected.Value, $"Unexpected live waypoint for {expected.Key}: {definition.Waypoint}.");
            }

            Console.WriteLine($"Live audit passed: widget {parsed.Version}, {parsed.Events.Count} events.");
        }

        private static void AssertThrows(string json, string message) {
            bool threw = false;
            try {
                OfficialEventTimerParser.Parse(json, requireProductionCounts: false);
            } catch {
                threw = true;
            }
            Assert(threw, message);
        }

        private static void Assert(bool condition, string message) {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
