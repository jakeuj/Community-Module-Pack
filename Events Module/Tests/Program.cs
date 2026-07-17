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

            bool productionGuardRejectedFixture = false;
            try {
                OfficialEventTimerParser.Parse(Fixture);
            } catch {
                productionGuardRejectedFixture = true;
            }
            Assert(productionGuardRejectedFixture, "Production sanity-count checks did not reject a tiny payload.");
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
