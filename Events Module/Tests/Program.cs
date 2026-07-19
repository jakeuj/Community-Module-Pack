using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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

            RunEventChatMessageFormatterTests();
            RunIconMatcherTests();
            RunRewardCatalogTests();
            RunModuleUpdateTests();

            bool productionGuardRejectedFixture = false;
            try {
                OfficialEventTimerParser.Parse(Fixture);
            } catch {
                productionGuardRejectedFixture = true;
            }
            Assert(productionGuardRejectedFixture, "Production sanity-count checks did not reject a tiny payload.");
        }

        private static void RunEventChatMessageFormatterTests() {
            var values = new EventChatMessageValues {
                Point = "[&BEwCAAA=]",
                EventZh = "吞噬托",
                EventEn = "Tequatl the Sunless",
                CategoryZh = "世界王",
                CategoryEn = "World Bosses",
                Time = "下午 07:30"
            };

            EventChatMessageFormatResult complete = EventChatMessageFormatter.Format(
                "{point}|{event}|{event_zh}|{event_en}|{category}|{category_zh}|{category_en}|{time}|{{literal}}",
                values
            );
            Assert(complete.IsValid, "A format containing all supported fields should be valid.");
            Assert(complete.Text == "[&BEwCAAA=]|吞噬托 / Tequatl the Sunless|吞噬托|Tequatl the Sunless|世界王 / World Bosses|世界王|World Bosses|下午 07:30|{literal}",
                   "Supported chat message fields or escaped braces were formatted incorrectly.");

            EventChatMessageFormatResult freeText = EventChatMessageFormatter.Format(
                "{point} 【{category_zh}】 {event}，{time} 開打，有人要一起嗎？",
                values
            );
            Assert(freeText.IsValid &&
                   freeText.Text == "[&BEwCAAA=] 【世界王】 吞噬托 / Tequatl the Sunless，下午 07:30 開打，有人要一起嗎？",
                   "Free text and punctuation should be preserved around formatted fields.");

            EventClipboardTextResult disabled = EventChatMessageFormatter.BuildClipboardText(
                false,
                "{unknown}",
                values
            );
            Assert(disabled.Text == values.Point && !disabled.UsedCustomFormat && !disabled.FellBackToPoint,
                   "A disabled custom format should preserve the original waypoint copy behavior.");

            EventClipboardTextResult enabled = EventChatMessageFormatter.BuildClipboardText(
                true,
                "{point} {event}",
                values
            );
            Assert(enabled.Text == "[&BEwCAAA=] 吞噬托 / Tequatl the Sunless" &&
                   enabled.UsedCustomFormat && !enabled.FellBackToPoint,
                   "A valid enabled format should produce a formatted chat message.");

            EventClipboardTextResult fallback = EventChatMessageFormatter.BuildClipboardText(
                true,
                "{event}",
                values
            );
            Assert(fallback.Text == values.Point && !fallback.UsedCustomFormat && fallback.FellBackToPoint &&
                   fallback.FormatResult.Failure == EventChatMessageFormatFailure.MissingPoint,
                   "An invalid enabled format should safely fall back to the original waypoint.");

            values.EventEn = values.EventZh;
            values.CategoryEn = values.CategoryZh;
            EventChatMessageFormatResult deduplicated = EventChatMessageFormatter.Format(
                "{point} {event} {category}",
                values
            );
            Assert(deduplicated.IsValid && deduplicated.Text == "[&BEwCAAA=] 吞噬托 世界王",
                   "Smart bilingual fields should not duplicate identical localized and English values.");

            AssertFormatFailure(null, EventChatMessageFormatFailure.EmptyFormat,
                                "An empty chat message format must be rejected.");
            AssertFormatFailure("{event} {time}", EventChatMessageFormatFailure.MissingPoint,
                                "A chat message format without {point} must be rejected.");
            AssertFormatFailure("{{point}}", EventChatMessageFormatFailure.MissingPoint,
                                "An escaped literal {point} must not satisfy the required field.");
            AssertFormatFailure("{point} {unknown}", EventChatMessageFormatFailure.UnknownField,
                                "Unknown chat message fields must be rejected.");
            AssertFormatFailure("{point", EventChatMessageFormatFailure.UnbalancedBraces,
                                "An unterminated opening brace must be rejected.");
            AssertFormatFailure("{point}}", EventChatMessageFormatFailure.UnbalancedBraces,
                                "An unmatched closing brace must be rejected.");
        }

        private static void AssertFormatFailure(string format,
                                                EventChatMessageFormatFailure expectedFailure,
                                                string message) {
            EventChatMessageFormatResult result = EventChatMessageFormatter.Format(
                format,
                new EventChatMessageValues { Point = "[&BEwCAAA=]" }
            );
            Assert(!result.IsValid && result.Failure == expectedFailure, message);
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

        private static void RunRewardCatalogTests() {
            string catalogJson = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "event-rewards.json"));
            EventRewardCatalog catalog = EventRewardCatalog.Parse(catalogJson);

            Assert(catalog.Count == 13, "The reward catalog should contain the 13 core world bosses.");

            EventRewardSummary golem = catalog.Match("[&BNQCAAA=]", "Renamed Golem");
            Assert(golem?.Id == "world-boss:golem-mark-ii", "Waypoint matching should identify Golem Mark II.");
            Assert(golem.DragoniteAmount == "15–25", "Golem Mark II should show its verified Dragonite Ore range.");

            EventRewardSummary megadestroyer = catalog.Match(null, "Megadestroyer");
            Assert(megadestroyer?.DragoniteAmount == "3–5", "English-name fallback should match Megadestroyer.");

            EventRewardSummary greatWurm = catalog.Match("[&BEEFAAA=]", "Jungle Wurm");
            EventRewardSummary evolvedWurm = catalog.Match("[&BKoBAAA=]", "Jungle Wurm");
            Assert(greatWurm?.Id == "world-boss:great-jungle-wurm", "The Great Jungle Wurm waypoint should stay distinct.");
            Assert(evolvedWurm?.Id == "world-boss:evolved-jungle-wurm", "The Evolved Jungle Wurm waypoint should stay distinct.");
            Assert(catalog.Match("[&AAAAAAA=]", "Unknown Event") == null, "Unlisted events must not receive reward claims.");

            AssertRewardCatalogThrows(
                catalogJson.Replace("\"world-boss:taidha-covington\"", "\"world-boss:megadestroyer\""),
                "Duplicate reward event IDs must be rejected."
            );
            AssertRewardCatalogThrows(
                catalogJson.Replace("2026-07-18", DateTime.UtcNow.Date.AddDays(2).ToString("yyyy-MM-dd")),
                "Reward verification dates beyond the worldwide time-zone boundary must be rejected."
            );
        }

        private static void RunModuleUpdateTests() {
            Assert(ModuleReleaseVersion.TryParseManifestVersion("1.0.9", out ModuleReleaseVersion baseVersion),
                   "A source manifest version without fork metadata should be accepted.");
            Assert(ModuleReleaseVersion.TryParseManifestVersion("1.0.9-fork.4", out ModuleReleaseVersion forkVersion),
                   "A packaged fork version should be accepted.");
            Assert(baseVersion.CompareTo(forkVersion) < 0, "A fork release should be newer than its base source version.");
            Assert(!ModuleReleaseVersion.TryParseManifestVersion("1.0", out _), "Malformed manifest versions must be rejected.");
            Assert(!ModuleReleaseVersion.TryParseReleaseTag("events-zh-tw-v1.0.9", out _),
                   "Release tags without fork metadata must be ignored.");
            Assert(!ModuleReleaseVersion.TryParseReleaseTag("events-zh-tw-v1.0.9-fork.5-test", out _),
                   "Test release tags must be ignored.");
            Assert(ModuleReleaseVersion.TryParseReleaseTag("events-zh-tw-v1.1.0-fork.1", out ModuleReleaseVersion nextBase),
                   "A stable fork release tag should be accepted.");
            Assert(nextBase.CompareTo(forkVersion) > 0, "Base module upgrades must take precedence over fork counters.");

            Assert(ModuleUpdatePolicy.ShouldAutomaticallyInstall(true, true, true, true),
                   "An enabled startup update should install a valid supported release.");
            Assert(!ModuleUpdatePolicy.ShouldAutomaticallyInstall(true, false, true, true),
                   "Disabling automatic updates must prevent startup installation.");
            Assert(!ModuleUpdatePolicy.ShouldAutomaticallyInstall(false, true, true, true),
                   "Enabling the setting after a completed check must not suddenly install an update.");
            Assert(!ModuleUpdatePolicy.ShouldAutomaticallyInstall(true, true, false, true),
                   "Debug and unpacked modules must not install updates.");

            string validDigest = "sha256:" + new string('a', 64);
            ModuleUpdateCheckResult available = CheckRelease(
                "1.0.9-fork.4",
                CreateReleaseJson("events-zh-tw-v1.0.9-fork.5", "Events.Module.bhm", validDigest)
            );
            Assert(available.UpdateAvailable, "A newer stable fork release should be offered.");
            Assert(available.Release.Sha256 == new string('A', 64), "The SHA-256 digest should be normalized for Blish HUD.");

            ModuleUpdateCheckResult fromBase = CheckRelease(
                "1.0.9",
                CreateReleaseJson("events-zh-tw-v1.0.9-fork.1", "Events.Module.bhm", validDigest)
            );
            Assert(fromBase.UpdateAvailable, "The first fork should update an upstream base-version package.");

            ModuleUpdateCheckResult same = CheckRelease(
                "1.0.9-fork.5",
                CreateReleaseJson("events-zh-tw-v1.0.9-fork.5", "Events.Module.bhm", validDigest)
            );
            Assert(!same.UpdateAvailable, "The same package version must not update itself.");

            ModuleUpdateCheckResult downgrade = CheckRelease(
                "1.1.0-fork.1",
                CreateReleaseJson("events-zh-tw-v1.0.99-fork.99", "Events.Module.bhm", validDigest)
            );
            Assert(!downgrade.UpdateAvailable, "Older base versions must never be installed, regardless of fork counter.");

            Assert(CheckRelease(
                "1.0.9-fork.4",
                CreateReleaseJson("events-zh-tw-v1.0.9-fork.5", "Events Module.bhm", validDigest)
            ).Failure == ModuleUpdateFailure.MissingAsset, "Only the exact Events.Module.bhm asset name should be accepted.");
            Assert(CheckRelease(
                "1.0.9-fork.4",
                CreateReleaseJson("events-zh-tw-v1.0.9-fork.5", "Events.Module.bhm", "sha256:1234")
            ).Failure == ModuleUpdateFailure.InvalidDigest, "Malformed release digests must be rejected.");
            Assert(CheckRelease(
                "1.0.9-fork.4",
                CreateReleaseJson(
                    "events-zh-tw-v1.0.9-fork.5",
                    "Events.Module.bhm",
                    validDigest,
                    "https://example.com/Events.Module.bhm"
                )
            ).Failure == ModuleUpdateFailure.InvalidAssetUrl, "Non-GitHub asset URLs must be rejected.");
            Assert(CheckRelease(
                "1.0.9-fork.4",
                CreateReleaseJson("events-zh-tw-v1.0.9-fork.5", "Events.Module.bhm", validDigest, draft: true)
            ).Failure == ModuleUpdateFailure.InvalidRelease, "Draft releases must be ignored.");
            Assert(CheckRelease(
                "1.0.9-fork.4",
                CreateReleaseJson("events-zh-tw-v1.0.9-fork.5", "Events.Module.bhm", validDigest, prerelease: true)
            ).Failure == ModuleUpdateFailure.InvalidRelease, "Prereleases must be ignored.");
            Assert(CheckRelease(
                "1.0.9-fork.4",
                CreateReleaseJson("events-zh-tw-v1.0.9-fork.5-test1", "Events.Module.bhm", validDigest)
            ).Failure == ModuleUpdateFailure.InvalidRelease, "Test-tag releases must be ignored.");

            using (var invalidCurrentService = new ModuleUpdateService(new StubHttpHandler((request, token) => {
                throw new InvalidOperationException("HTTP should not be called for an invalid installed version.");
            }))) {
                ModuleUpdateCheckResult invalidCurrent = invalidCurrentService.CheckAsync("invalid", CancellationToken.None)
                                                                             .GetAwaiter()
                                                                             .GetResult();
                Assert(invalidCurrent.Failure == ModuleUpdateFailure.InvalidCurrentVersion,
                       "Invalid installed versions should stop before making an HTTP request.");
            }

            AssertUpdateCheckThrows<Newtonsoft.Json.JsonReaderException>(
                new StubHttpHandler((request, token) => Task.FromResult(Response(HttpStatusCode.OK, "not-json"))),
                "Invalid GitHub JSON must fail without producing an update."
            );
            AssertUpdateCheckThrows<HttpRequestException>(
                new StubHttpHandler((request, token) => Task.FromResult(Response(HttpStatusCode.Forbidden, "forbidden"))),
                "GitHub HTTP 403 must fail the update check."
            );
            AssertUpdateCheckThrows<HttpRequestException>(
                new StubHttpHandler((request, token) => Task.FromResult(Response(HttpStatusCode.NotFound, "missing"))),
                "GitHub HTTP 404 must fail the update check."
            );

            using (var timeoutService = new ModuleUpdateService(
                new StubHttpHandler(async (request, token) => {
                    await Task.Delay(Timeout.Infinite, token);
                    return Response(HttpStatusCode.OK, "{}");
                }),
                TimeSpan.FromMilliseconds(25)
            )) {
                bool timedOut = false;
                try {
                    timeoutService.CheckAsync("1.0.9-fork.4", CancellationToken.None).GetAwaiter().GetResult();
                } catch (OperationCanceledException) {
                    timedOut = true;
                }
                Assert(timedOut, "The GitHub update check must honor its HTTP timeout.");
            }

            using (var cancellation = new CancellationTokenSource())
            using (var cancellationService = new ModuleUpdateService(new StubHttpHandler(async (request, token) => {
                await Task.Delay(Timeout.Infinite, token);
                return Response(HttpStatusCode.OK, "{}");
            }))) {
                Task<ModuleUpdateCheckResult> check = cancellationService.CheckAsync("1.0.9-fork.4", cancellation.Token);
                cancellation.Cancel();
                bool canceled = false;
                try {
                    check.GetAwaiter().GetResult();
                } catch (OperationCanceledException) {
                    canceled = true;
                }
                Assert(canceled, "Unloading the module must be able to cancel an in-flight GitHub request.");
            }
        }

        private static ModuleUpdateCheckResult CheckRelease(string currentVersion, string json) {
            using (var service = new ModuleUpdateService(new StubHttpHandler((request, token) =>
                Task.FromResult(Response(HttpStatusCode.OK, json))))) {
                return service.CheckAsync(currentVersion, CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        private static string CreateReleaseJson(
            string tag,
            string assetName,
            string digest,
            string assetUrl = null,
            bool draft = false,
            bool prerelease = false
        ) {
            assetUrl = assetUrl ?? $"https://github.com/jakeuj/Community-Module-Pack/releases/download/{tag}/Events.Module.bhm";
            return new JObject {
                ["tag_name"] = tag,
                ["draft"] = draft,
                ["prerelease"] = prerelease,
                ["assets"] = new JArray(new JObject {
                    ["name"] = assetName,
                    ["browser_download_url"] = assetUrl,
                    ["digest"] = digest
                })
            }.ToString();
        }

        private static HttpResponseMessage Response(HttpStatusCode statusCode, string content) {
            return new HttpResponseMessage(statusCode) { Content = new StringContent(content) };
        }

        private static void AssertUpdateCheckThrows<TException>(HttpMessageHandler handler, string message)
            where TException : Exception {
            bool threw = false;
            using (var service = new ModuleUpdateService(handler)) {
                try {
                    service.CheckAsync("1.0.9-fork.4", CancellationToken.None).GetAwaiter().GetResult();
                } catch (TException) {
                    threw = true;
                }
            }
            Assert(threw, message);
        }

        private sealed class StubHttpHandler : HttpMessageHandler {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

            public StubHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) {
                _sendAsync = sendAsync;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
                return _sendAsync(request, cancellationToken);
            }
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

            EventRewardCatalog rewardCatalog = EventRewardCatalog.Parse(
                File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "event-rewards.json"))
            );
            int matchedRewardCount = parsed.Events
                                           .Select(item => rewardCatalog.Match(item.Waypoint, item.Name))
                                           .Where(reward => reward != null)
                                           .Select(reward => reward.Id)
                                           .Distinct(StringComparer.Ordinal)
                                           .Count();
            Assert(matchedRewardCount == 13,
                   $"Expected all 13 core world boss rewards to match the live Widget; matched {matchedRewardCount}.");

            using (var updateService = new ModuleUpdateService()) {
                ModuleUpdateCheckResult updateResult = updateService.CheckAsync("0.0.0", CancellationToken.None)
                                                                          .GetAwaiter()
                                                                          .GetResult();
                Assert(updateResult.Failure == ModuleUpdateFailure.None && updateResult.Release != null,
                       "The live latest stable GitHub Release must pass the updater's tag, asset, URL, and digest validation.");
                Assert(updateResult.UpdateAvailable, "The current stable fork release should be newer than the live-audit baseline.");
                Console.WriteLine($"Live updater audit passed: {updateResult.Release.TagName}, SHA-256 {updateResult.Release.Sha256}.");
            }

            Console.WriteLine($"Live audit passed: widget {parsed.Version}, {parsed.Events.Count} events, {matchedRewardCount} reward matches.");
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

        private static void AssertRewardCatalogThrows(string json, string message) {
            bool threw = false;
            try {
                EventRewardCatalog.Parse(json);
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
