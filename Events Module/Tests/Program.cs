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

        private const string WikiLinkFixture = @"{
  'config': { 'version': 'test-wiki-links' },
  'events': {
    'wiki-links': {
      'category': 'Test',
      'name': 'Wiki Links',
      'segments': {
        '1': { 'name': 'Mount Balrior', 'link': 'Convergence: Mount Balrior' },
        '2': { 'name': 'Outer Nayos', 'link': 'Convergence: Outer Nayos#Walkthrough' },
        '3': { 'name': 'Official Absolute', 'link': 'https://wiki.guildwars2.com/wiki/Alpha_Event#Walkthrough' },
        '4': { 'name': 'Insecure Absolute', 'link': 'http://wiki.guildwars2.com/wiki/Alpha_Event' },
        '5': { 'name': 'External Absolute', 'link': 'https://example.com/wiki/Alpha_Event' },
        '6': { 'name': 'Malformed Absolute', 'link': 'https://[invalid' }
      },
      'sequences': {
        'partial': [
          { 'r': 1, 'd': 240 },
          { 'r': 2, 'd': 240 },
          { 'r': 3, 'd': 240 },
          { 'r': 4, 'd': 240 },
          { 'r': 5, 'd': 240 },
          { 'r': 6, 'd': 240 }
        ],
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

            OfficialEventTimerParseResult wikiLinks = OfficialEventTimerParser.Parse(WikiLinkFixture, requireProductionCounts: false);
            Dictionary<string, OfficialEventDefinition> wikiLinksById = wikiLinks.Events.ToDictionary(item => item.StableId, StringComparer.Ordinal);
            Assert(wikiLinksById["wiki:wiki-links:1"].Wiki == "https://wiki.guildwars2.com/wiki/Convergence%3A_Mount_Balrior",
                   "Wiki titles containing a colon must not be mistaken for absolute URI schemes.");
            Assert(wikiLinksById["wiki:wiki-links:2"].Wiki == "https://wiki.guildwars2.com/wiki/Convergence%3A_Outer_Nayos#Walkthrough",
                   "Wiki titles containing a colon should preserve anchors.");
            Assert(wikiLinksById["wiki:wiki-links:3"].Wiki == "https://wiki.guildwars2.com/wiki/Alpha_Event#Walkthrough",
                   "Official absolute HTTPS Wiki URLs should be preserved.");
            Assert(string.IsNullOrEmpty(wikiLinksById["wiki:wiki-links:4"].Wiki),
                   "Insecure absolute Wiki URLs must be rejected.");
            Assert(string.IsNullOrEmpty(wikiLinksById["wiki:wiki-links:5"].Wiki),
                   "Absolute URLs outside the official Wiki host must be rejected.");
            Assert(string.IsNullOrEmpty(wikiLinksById["wiki:wiki-links:6"].Wiki),
                   "Malformed absolute URLs must be rejected.");

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
            const string currentDefault =
                "{point} 【{category_zh}】 {event} {time} {reward}";
            var values = new EventChatMessageValues {
                Point = "[&BEwCAAA=]",
                EventZh = "吞噬托",
                EventEn = "Tequatl the Sunless",
                CategoryZh = "世界王",
                CategoryEn = "World Bosses",
                Time = "下午 07:30",
                Reward = "保底：稀有/特異≥1、龍晶礦15–25"
            };

            EventChatMessageFormatResult complete = EventChatMessageFormatter.Format(
                "{point}|{event}|{event_zh}|{event_en}|{category}|{category_zh}|{category_en}|{time}|{reward}|{{literal}}",
                values
            );
            Assert(complete.IsValid, "A format containing all supported fields should be valid.");
            Assert(complete.Text == "[&BEwCAAA=]|吞噬托 / Tequatl the Sunless|吞噬托|Tequatl the Sunless|世界王 / World Bosses|世界王|World Bosses|下午 07:30|保底：稀有/特異≥1、龍晶礦15–25|{literal}",
                   "Supported chat message fields or escaped braces were formatted incorrectly.");

            EventChatMessageFormatResult freeText = EventChatMessageFormatter.Format(
                "  {point} 【{category_zh}】 {event}，{time} 開打，有人要一起嗎？ {reward}  ",
                values
            );
            Assert(freeText.IsValid &&
                   freeText.Text == "[&BEwCAAA=] 【世界王】 吞噬托 / Tequatl the Sunless，下午 07:30 開打，有人要一起嗎？ 保底：稀有/特異≥1、龍晶礦15–25",
                   "Free text and punctuation should be preserved while outer whitespace is trimmed.");

            EventChatMessageFormatResult currentDefaultWithReward = EventChatMessageFormatter.Format(
                currentDefault,
                values
            );
            Assert(currentDefaultWithReward.IsValid &&
                   currentDefaultWithReward.Text == "[&BEwCAAA=] 【世界王】 吞噬托 / Tequatl the Sunless 下午 07:30 保底：稀有/特異≥1、龍晶礦15–25",
                   "The current default should preserve one space between time and reward.");

            string rewardSummary = values.Reward;
            values.Reward = string.Empty;
            EventClipboardTextResult noReward = EventChatMessageFormatter.BuildClipboardText(
                true,
                "  " + currentDefault + "  ",
                values
            );
            Assert(noReward.Text ==
                   "[&BEwCAAA=] 【世界王】 吞噬托 / Tequatl the Sunless 下午 07:30" &&
                   noReward.UsedCustomFormat && !noReward.FellBackToPoint,
                   "The default format should omit an unlisted reward without leaving outer whitespace or falling back.");
            values.Reward = rewardSummary;

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

            Assert(EventChatMessageFormatter.ContainsField("{point} {reward}", "reward"),
                   "An unescaped reward field should be detected.");
            Assert(!EventChatMessageFormatter.ContainsField("{point} {{reward}}", "reward"),
                   "An escaped literal reward field should not affect preview selection.");
            Assert(!EventChatMessageFormatter.ContainsField("{point} {Reward}", "reward"),
                   "Chat message field detection should remain case-sensitive.");

            string preferredPreview = EventChatMessagePreviewSelector.Select(
                new[] { "next-event", "rewarded-event" },
                "{point} {reward}",
                candidate => candidate == "rewarded-event"
            );
            Assert(preferredPreview == "rewarded-event",
                   "A reward format should preview the next candidate with verified reward data.");
            Assert(EventChatMessagePreviewSelector.Select(
                new[] { "next-event", "rewarded-event" },
                "{point} {{reward}}",
                candidate => candidate == "rewarded-event"
            ) == "next-event", "An escaped reward literal should keep the normal next-event preview.");
            Assert(EventChatMessagePreviewSelector.Select(
                new[] { "next-event", "later-event" },
                "{point} {reward}",
                candidate => false
            ) == "next-event", "Reward preview selection should fall back when no candidate has reward data.");

            const string oldEnglishDefault =
                "{point} [{category_zh}] {event}, starting at {time}. Anyone want to join?";
            const string oldChineseDefault =
                "{point} 【{category_zh}】 {event}，{time} 開打，有人要一起嗎？";
            const string previousEnglishDefault =
                "{point} [{category_zh}] {event}, starting at {time}. Anyone want to join? {reward}";
            const string previousChineseDefault =
                "{point} 【{category_zh}】 {event}，{time} 開打，有人要一起嗎？ {reward}";
            const string previousDoubleSpaceDefault =
                "{point} 【{category_zh}】 {event} {time}  {reward}";
            Assert(EventChatMessageFormatter.MigrateLegacyDefaultFormat(oldEnglishDefault, currentDefault) == currentDefault,
                   "The legacy neutral default should migrate to the current localized default.");
            Assert(EventChatMessageFormatter.MigrateLegacyDefaultFormat(oldChineseDefault, currentDefault) == currentDefault,
                   "The legacy Chinese default should migrate to the current localized default.");
            Assert(EventChatMessageFormatter.MigrateLegacyDefaultFormat(previousEnglishDefault, currentDefault) == currentDefault,
                   "The previous neutral reward default should migrate to the current localized default.");
            Assert(EventChatMessageFormatter.MigrateLegacyDefaultFormat(previousChineseDefault, currentDefault) == currentDefault,
                   "The previous Chinese reward default should migrate to the current localized default.");
            Assert(EventChatMessageFormatter.MigrateLegacyDefaultFormat(previousDoubleSpaceDefault, currentDefault) == currentDefault,
                   "The previous double-space default should migrate to the current localized default.");
            Assert(EventChatMessageFormatter.MigrateLegacyDefaultFormat("{point} my custom text", currentDefault) ==
                   "{point} my custom text", "User-edited chat formats must not be migrated.");

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
            JObject catalogDocument = JObject.Parse(catalogJson);

            Assert((int)catalogDocument["version"] == 3, "The reward catalog should use schema v3.");
            Assert(catalog.Count == 17, "The reward catalog should contain 13 core world bosses and 4 fixed-coin events.");

            EventRewardSummary golem = catalog.Match(null, "[&BNQCAAA=]", true, "Golem Mark II");
            Assert(golem?.Id == "world-boss:golem-mark-ii", "A unique waypoint and matching alias should identify Golem Mark II.");
            Assert(golem.DragoniteAmount == "15–25", "Golem Mark II should show its verified Dragonite Ore range.");
            Assert(golem.MinimumRareOrExoticItems == 1 &&
                   golem.RareOrExoticLimit == EventRewardLimit.AccountDaily &&
                   golem.DragoniteLimit == EventRewardLimit.CharacterDaily,
                   "World-boss rare gear and Dragonite limits should remain independent and explicit.");

            EventRewardSummary megadestroyer = catalog.Match(null, null, false, "Megadestroyer");
            Assert(megadestroyer?.DragoniteAmount == "3–5", "English-name fallback should match Megadestroyer.");

            EventRewardSummary greatWurm = catalog.Match(null, "[&BEEFAAA=]", true, "Jungle Wurm");
            EventRewardSummary evolvedWurm = catalog.Match(null, "[&BKoBAAA=]", true, "Triple Trouble");
            Assert(greatWurm?.Id == "world-boss:great-jungle-wurm", "The Great Jungle Wurm waypoint should stay distinct.");
            Assert(evolvedWurm?.Id == "world-boss:evolved-jungle-wurm", "The Evolved Jungle Wurm waypoint should stay distinct.");
            Assert(evolvedWurm?.CompactDragoniteAmount == "1–15+",
                   "The compact Evolved Jungle Wurm amount should retain its special marker for chat summaries.");
            EventRewardSummary claw = catalog.Match(null, "[&BHoCAAA=]", true, "Claw of Jormag");
            Assert(claw?.CompactDragoniteAmount == "15–24*",
                   "The compact Claw of Jormag amount should retain its special marker for chat summaries.");
            Assert(catalog.Match(null, "[&AAAAAAA=]", true, "Unknown Event") == null,
                   "Unlisted events must not receive reward claims.");

            EventRewardSummary dragonstorm = catalog.Match(
                "wiki:public-eotn:3",
                "[&BAkMAAA=]",
                false,
                "Wrong shared-waypoint name"
            );
            EventRewardSummary twistedMarionette = catalog.Match(
                "wiki:public-eotn:1",
                "[&BAkMAAA=]",
                false,
                "Wrong shared-waypoint name"
            );
            Assert(dragonstorm?.Id == "public-instance:dragonstorm" &&
                   twistedMarionette?.Id == "public-instance:twisted-marionette",
                   "Stable IDs must distinguish Dragonstorm and the Twisted Marionette at their shared waypoint.");
            Assert(dragonstorm.MinimumRareOrExoticItems == 2 &&
                   dragonstorm.RareOrExoticLimit == EventRewardLimit.AccountDaily &&
                   dragonstorm.GuaranteedCoinCopper == 20000 &&
                   dragonstorm.CoinLimit == EventRewardLimit.AccountDaily &&
                   dragonstorm.VerifiedOn == new DateTime(2026, 7, 19),
                   "Dragonstorm should expose its verified account-daily rare gear and 2G rewards.");
            Assert(twistedMarionette.MinimumRareOrExoticItems == 2 &&
                   twistedMarionette.RareOrExoticLimit == EventRewardLimit.AccountDaily &&
                   twistedMarionette.DragoniteAmount == "15" &&
                   twistedMarionette.DragoniteLimit == EventRewardLimit.CharacterDaily &&
                   twistedMarionette.Sources.Count == 3,
                   "The Twisted Marionette should expose independent rare, Dragonite, and multi-source data.");
            Assert(golem.VerifiedOn == new DateTime(2026, 7, 18),
                   "Existing world-boss entries should retain their original catalog verification date.");
            Assert(catalog.Match("local:Meta Event:Dragonstorm (Public)", "[&BAkMAAA=]", false, "Dragonstorm (Public)")?.Id ==
                   "public-instance:dragonstorm",
                   "Bundled fallback data should match Dragonstorm by its normalized English alias.");
            Assert(catalog.Match(null, "[&BAkMAAA=]", false, "Unknown Event") == null,
                   "A shared public-instance waypoint must not infer a reward without a stable ID or known alias.");

            EventRewardSummary mountBalrior = catalog.Match(
                "wiki:public-con:1",
                "[&BK4OAAA=]",
                true,
                "Mount Balrior"
            );
            EventRewardSummary outerNayos = catalog.Match(
                "wiki:public-con:2",
                "[&BB8OAAA=]",
                false,
                "Outer Nayos"
            );
            Assert(mountBalrior?.MinimumRareOrExoticItems == 3 &&
                   mountBalrior.RareOrExoticLimit == EventRewardLimit.AccountDaily &&
                   mountBalrior.GuaranteedCoinCopper == 20000 &&
                   outerNayos?.GuaranteedCoinCopper == 20000,
                   "The Convergences should expose only their verified daily-scope reward components.");
            Assert(outerNayos.MinimumRareOrExoticItems == null &&
                   string.IsNullOrWhiteSpace(outerNayos.DragoniteAmount),
                   "Outer Nayos daily scope should remain fixed-coin only.");
            Assert(catalog.Match("local:Public Instances:Outer Nayos", "[&BB8OAAA=]", false, "Outer Nayos")?.Id ==
                   "public-instance:convergence-outer-nayos",
                   "Bundled Outer Nayos should match by its verified English alias.");

            var falsePositiveEvents = new Dictionary<string, string>(StringComparer.Ordinal) {
                ["wiki:soto-wt:1"] = "Target Practice",
                ["wiki:soto-wt:2"] = "Fly by Night",
                ["wiki:soto-wt:3"] = "Target Practice & Fly by Night"
            };
            foreach (var falsePositive in falsePositiveEvents) {
                Assert(catalog.Match(falsePositive.Key, "[&BB8OAAA=]", false, falsePositive.Value) == null,
                       falsePositive.Key + " must not inherit the Outer Nayos reward through a shared waypoint.");
            }
            Assert(catalog.Match("local:Adventure:Target Practice", "[&BB8OAAA=]", false, "Target Practice") == null &&
                   catalog.Match("local:Adventure:Fly by Night", "[&BB8OAAA=]", false, "Fly by Night") == null,
                   "Bundled Wizard's Tower adventures must not inherit the Outer Nayos reward.");
            Assert(catalog.Match(null, "[&BK4OAAA=]", true, "Wrong event name") == null,
                   "Even a globally unique waypoint must have a compatible verified alias.");

            Assert(EventRewardTextFormatter.FormatCoin(20000) == "2G" &&
                   EventRewardTextFormatter.FormatCoin(20105) == "2G 1S 5C",
                   "Coin formatting must use exact integer copper conversion.");
            Assert(BuildChineseCompactReward(golem) == "保底：稀有/特異≥1、龍晶礦15–25",
                   "Existing world-boss compact reward text must remain unchanged.");
            Assert(BuildChineseCompactReward(dragonstorm) == "保底：稀有/特異≥2、2G（帳號每日）",
                   "Dragonstorm should include its account-daily rare gear and fixed coin.");
            Assert(BuildChineseCompactReward(twistedMarionette) ==
                   "保底：稀有/特異≥2、龍晶礦15、2G（帳號每日）",
                   "The Twisted Marionette should include all three daily guarantee components.");
            Assert(BuildChineseCompactReward(mountBalrior) == "保底：稀有/特異≥3、2G（帳號每日）",
                   "Mount Balrior should include its account-daily rare gear and fixed coin.");
            Assert(BuildChineseCompactReward(outerNayos) == "保底：2G（帳號每日）",
                   "Outer Nayos should remain fixed-coin only under the daily guarantee scope.");
            Assert(BuildEnglishCompactReward(dragonstorm) ==
                   "Guaranteed: rare/exotic ≥2, 2G (daily per account)" &&
                   BuildEnglishCompactReward(twistedMarionette) ==
                   "Guaranteed: rare/exotic ≥2, Dragonite 15, 2G (daily per account)" &&
                   BuildEnglishCompactReward(mountBalrior) ==
                   "Guaranteed: rare/exotic ≥3, 2G (daily per account)" &&
                   BuildEnglishCompactReward(outerNayos) ==
                   "Guaranteed: 2G (daily per account)",
                   "All four fixed-coin events should have stable neutral compact summaries.");
            Assert(BuildChineseCompactReward(null) == string.Empty,
                   "Unlisted events should continue to produce an empty compact reward summary.");

            string twistedDetails = EventRewardTextFormatter.BuildDetailedSummary(
                twistedMarionette,
                CreateChineseRewardDetailFormats()
            );
            string newline = Environment.NewLine;
            Assert(twistedDetails ==
                   "事件獎勵資訊" + newline +
                   "保底稀有或特異裝備：至少 2 件" + newline +
                   "額外獎勵寶箱：每帳號每日一次。" + newline + newline +
                   "地面寶箱保底龍晶礦：15" + newline +
                   "地面寶箱：每角色每日一次。" + newline + newline +
                   "保證金幣：2G" + newline +
                   "金幣獎勵：每帳號每日一次。" + newline + newline +
                   "資料來源：Guild Wars 2 Wiki — The Twisted Marionette、Hoard of the Marionette Warden V、Marionette Chest" + newline +
                   "查核日期：2026-07-19",
                   "The detailed tooltip should preserve component limits, all Wiki sources, and the verification date. Actual: " +
                   twistedDetails.Replace("\r", "\\r").Replace("\n", "\\n"));

            AssertRewardCatalogThrows(
                catalogJson.Replace("\"world-boss:taidha-covington\"", "\"world-boss:megadestroyer\""),
                "Duplicate reward event IDs must be rejected."
            );

            JObject invalidCatalog = JObject.Parse(catalogJson);
            JArray invalidEvents = (JArray)invalidCatalog["events"];
            JObject invalidDragonstorm = FindRewardEvent(invalidEvents, "public-instance:dragonstorm");
            invalidDragonstorm["stableIds"] = new JArray("wiki:public-con:1");
            AssertRewardCatalogThrows(invalidCatalog.ToString(), "Duplicate reward stable IDs must be rejected.");

            invalidCatalog = JObject.Parse(catalogJson);
            invalidEvents = (JArray)invalidCatalog["events"];
            invalidDragonstorm = FindRewardEvent(invalidEvents, "public-instance:dragonstorm");
            invalidDragonstorm["names"] = new JArray("Outer Nayos");
            AssertRewardCatalogThrows(invalidCatalog.ToString(), "Duplicate normalized reward aliases must be rejected.");

            invalidCatalog = JObject.Parse(catalogJson);
            invalidEvents = (JArray)invalidCatalog["events"];
            invalidDragonstorm = FindRewardEvent(invalidEvents, "public-instance:dragonstorm");
            invalidDragonstorm["minimumRareOrExoticItems"] = 0;
            AssertRewardCatalogThrows(invalidCatalog.ToString(), "Zero guaranteed rare or exotic count must be rejected.");

            invalidCatalog = JObject.Parse(catalogJson);
            invalidEvents = (JArray)invalidCatalog["events"];
            invalidDragonstorm = FindRewardEvent(invalidEvents, "public-instance:dragonstorm");
            invalidDragonstorm.Property("rareOrExoticLimit").Remove();
            AssertRewardCatalogThrows(invalidCatalog.ToString(), "A reward component without its limit must be rejected.");

            invalidCatalog = JObject.Parse(catalogJson);
            invalidEvents = (JArray)invalidCatalog["events"];
            JObject invalidOuterNayos = FindRewardEvent(invalidEvents, "public-instance:convergence-outer-nayos");
            invalidOuterNayos["dragoniteLimit"] = "character-daily";
            AssertRewardCatalogThrows(invalidCatalog.ToString(), "A limit without its reward component must be rejected.");

            invalidCatalog = JObject.Parse(catalogJson);
            invalidEvents = (JArray)invalidCatalog["events"];
            invalidDragonstorm = FindRewardEvent(invalidEvents, "public-instance:dragonstorm");
            invalidDragonstorm["guaranteedCoinCopper"] = 0;
            AssertRewardCatalogThrows(invalidCatalog.ToString(), "Zero guaranteed coin must be rejected.");
            invalidDragonstorm["guaranteedCoinCopper"] = -1;
            AssertRewardCatalogThrows(invalidCatalog.ToString(), "Negative guaranteed coin must be rejected.");

            invalidCatalog = JObject.Parse(catalogJson);
            invalidEvents = (JArray)invalidCatalog["events"];
            invalidDragonstorm = FindRewardEvent(invalidEvents, "public-instance:dragonstorm");
            invalidDragonstorm["coinLimit"] = "weekly";
            AssertRewardCatalogThrows(invalidCatalog.ToString(), "Unsupported guaranteed coin limits must be rejected.");

            invalidCatalog = JObject.Parse(catalogJson);
            invalidEvents = (JArray)invalidCatalog["events"];
            invalidOuterNayos = FindRewardEvent(invalidEvents, "public-instance:convergence-outer-nayos");
            invalidOuterNayos.Property("guaranteedCoinCopper").Remove();
            AssertRewardCatalogThrows(invalidCatalog.ToString(), "A coin limit without a coin amount must be rejected.");
            invalidOuterNayos.Property("coinLimit").Remove();
            AssertRewardCatalogThrows(invalidCatalog.ToString(), "Entries without supported reward components must be rejected.");

            invalidCatalog = JObject.Parse(catalogJson);
            invalidEvents = (JArray)invalidCatalog["events"];
            invalidDragonstorm = FindRewardEvent(invalidEvents, "public-instance:dragonstorm");
            invalidDragonstorm["sources"] = new JArray();
            AssertRewardCatalogThrows(invalidCatalog.ToString(), "Reward entries without official sources must be rejected.");

            invalidCatalog = JObject.Parse(catalogJson);
            invalidEvents = (JArray)invalidCatalog["events"];
            invalidDragonstorm = FindRewardEvent(invalidEvents, "public-instance:dragonstorm");
            ((JObject)((JArray)invalidDragonstorm["sources"])[0])["url"] = "https://example.com/not-the-wiki";
            AssertRewardCatalogThrows(invalidCatalog.ToString(), "Non-Wiki reward sources must be rejected.");

            invalidCatalog = JObject.Parse(catalogJson);
            invalidEvents = (JArray)invalidCatalog["events"];
            JObject invalidMarionette = FindRewardEvent(invalidEvents, "public-instance:twisted-marionette");
            JArray marionetteSources = (JArray)invalidMarionette["sources"];
            marionetteSources.Add(marionetteSources[0].DeepClone());
            AssertRewardCatalogThrows(invalidCatalog.ToString(), "Duplicate reward sources must be rejected.");

            invalidCatalog = JObject.Parse(catalogJson);
            invalidEvents = (JArray)invalidCatalog["events"];
            invalidDragonstorm = FindRewardEvent(invalidEvents, "public-instance:dragonstorm");
            invalidDragonstorm.Property("minimumRareOrExoticItems").Remove();
            invalidDragonstorm.Property("rareOrExoticLimit").Remove();
            invalidDragonstorm.Property("guaranteedCoinCopper").Remove();
            invalidDragonstorm.Property("coinLimit").Remove();
            AssertRewardCatalogThrows(invalidCatalog.ToString(), "Entries without supported reward components must be rejected.");

            invalidCatalog = JObject.Parse(catalogJson);
            invalidCatalog["verifiedOn"] = DateTime.UtcNow.Date.AddDays(2).ToString("yyyy-MM-dd");
            AssertRewardCatalogThrows(invalidCatalog.ToString(),
                                      "Catalog verification dates beyond the worldwide time-zone boundary must be rejected.");
            invalidCatalog = JObject.Parse(catalogJson);
            invalidEvents = (JArray)invalidCatalog["events"];
            invalidDragonstorm = FindRewardEvent(invalidEvents, "public-instance:dragonstorm");
            invalidDragonstorm["verifiedOn"] = DateTime.UtcNow.Date.AddDays(2).ToString("yyyy-MM-dd");
            AssertRewardCatalogThrows(invalidCatalog.ToString(),
                                      "Per-event verification dates beyond the worldwide time-zone boundary must be rejected.");
        }

        private static string BuildChineseCompactReward(EventRewardSummary reward) {
            return EventRewardTextFormatter.BuildCompactSummary(
                reward,
                "保底：",
                "、",
                "稀有/特異≥{0}",
                "龍晶礦{0}",
                "{0}（帳號每日）"
            );
        }

        private static string BuildEnglishCompactReward(EventRewardSummary reward) {
            return EventRewardTextFormatter.BuildCompactSummary(
                reward,
                "Guaranteed: ",
                ", ",
                "rare/exotic ≥{0}",
                "Dragonite {0}",
                "{0} (daily per account)"
            );
        }

        private static EventRewardDetailFormats CreateChineseRewardDetailFormats() {
            return new EventRewardDetailFormats {
                Title = "事件獎勵資訊",
                RareOrExoticFormat = "保底稀有或特異裝備：至少 {0} 件",
                RareAccountDailyLimit = "額外獎勵寶箱：每帳號每日一次。",
                RareCharacterDailyLimit = "獎勵：每角色每日一次。",
                DragoniteFormat = "地面寶箱保底龍晶礦：{0}",
                DragoniteAccountDailyLimit = "獎勵：每帳號每日一次。",
                DragoniteCharacterDailyLimit = "地面寶箱：每角色每日一次。",
                CoinFormat = "保證金幣：{0}",
                CoinAccountDailyLimit = "金幣獎勵：每帳號每日一次。",
                CoinCharacterDailyLimit = "獎勵：每角色每日一次。",
                SourceFormat = "資料來源：Guild Wars 2 Wiki — {0}",
                SourceSeparator = "、",
                VerifiedFormat = "查核日期：{0:yyyy-MM-dd}",
                NoteResolver = key => key
            };
        }

        private static JObject FindRewardEvent(JArray events, string id) {
            return events.Children<JObject>().Single(item => (string)item["id"] == id);
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
                ["wiki:lws5-gv:1"] = "[&BA4MAAA=]",
                ["wiki:public-eotn:1"] = "[&BAkMAAA=]",
                ["wiki:public-eotn:3"] = "[&BAkMAAA=]",
                ["wiki:public-con:1"] = "[&BK4OAAA=]",
                ["wiki:public-con:2"] = "[&BB8OAAA=]"
            };

            foreach (var expected in expectedWaypoints) {
                Assert(byId.TryGetValue(expected.Key, out OfficialEventDefinition definition), "Missing live official event " + expected.Key + ".");
                Assert(definition.Waypoint == expected.Value, $"Unexpected live waypoint for {expected.Key}: {definition.Waypoint}.");
            }

            var expectedWikis = new Dictionary<string, string>(StringComparer.Ordinal) {
                ["wiki:public-con:1"] = "https://wiki.guildwars2.com/wiki/Convergence%3A_Mount_Balrior",
                ["wiki:public-con:2"] = "https://wiki.guildwars2.com/wiki/Convergence%3A_Outer_Nayos"
            };

            foreach (var expected in expectedWikis) {
                Assert(byId.TryGetValue(expected.Key, out OfficialEventDefinition definition), "Missing live official event " + expected.Key + ".");
                Assert(definition.Wiki == expected.Value, $"Unexpected live Wiki URL for {expected.Key}: {definition.Wiki}.");
            }

            EventRewardCatalog rewardCatalog = EventRewardCatalog.Parse(
                File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "event-rewards.json"))
            );
            Dictionary<string, int> waypointCounts = parsed.Events
                .Where(item => !string.IsNullOrWhiteSpace(item.Waypoint))
                .GroupBy(item => item.Waypoint.Trim(), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var matchedRewards = parsed.Events
                                       .Select(item => new {
                                           Event = item,
                                           Reward = rewardCatalog.Match(
                                               item.StableId,
                                               item.Waypoint,
                                               IsWaypointUnique(item.Waypoint, waypointCounts),
                                               item.Name
                                           )
                                       })
                                       .Where(match => match.Reward != null)
                                       .ToList();
            int matchedRewardCount = matchedRewards.Count;
            Assert(matchedRewardCount == 17,
                   $"Expected 13 core world bosses and 4 fixed-coin events to match the live Widget; matched {matchedRewardCount}.");

            var expectedRewardMappings = new Dictionary<string, string>(StringComparer.Ordinal) {
                ["wiki:core-wb:1"] = "world-boss:taidha-covington",
                ["wiki:core-wb:2"] = "world-boss:svanir-shaman-chief",
                ["wiki:core-wb:3"] = "world-boss:megadestroyer",
                ["wiki:core-wb:4"] = "world-boss:fire-elemental",
                ["wiki:core-wb:5"] = "world-boss:the-shatterer",
                ["wiki:core-wb:6"] = "world-boss:great-jungle-wurm",
                ["wiki:core-wb:7"] = "world-boss:modniir-ulgoth",
                ["wiki:core-wb:8"] = "world-boss:shadow-behemoth",
                ["wiki:core-wb:9"] = "world-boss:claw-of-jormag",
                ["wiki:core-wb:10"] = "world-boss:golem-mark-ii",
                ["wiki:core-hwb:1"] = "world-boss:evolved-jungle-wurm",
                ["wiki:core-hwb:2"] = "world-boss:karka-queen",
                ["wiki:core-hwb:3"] = "world-boss:tequatl-the-sunless",
                ["wiki:public-eotn:1"] = "public-instance:twisted-marionette",
                ["wiki:public-eotn:3"] = "public-instance:dragonstorm",
                ["wiki:public-con:1"] = "public-instance:convergence-mount-balrior",
                ["wiki:public-con:2"] = "public-instance:convergence-outer-nayos"
            };
            Assert(matchedRewards.Count == expectedRewardMappings.Count,
                   "The live reward mapping must contain exactly the allowlisted stable IDs.");
            foreach (var mapping in expectedRewardMappings) {
                Assert(matchedRewards.Any(match =>
                           match.Event.StableId == mapping.Key && match.Reward.Id == mapping.Value),
                       $"Missing or incorrect live reward mapping {mapping.Key} -> {mapping.Value}.");
            }
            foreach (string falsePositiveStableId in new[] { "wiki:soto-wt:1", "wiki:soto-wt:2", "wiki:soto-wt:3" }) {
                Assert(matchedRewards.All(match => match.Event.StableId != falsePositiveStableId),
                       falsePositiveStableId + " must not inherit the Outer Nayos fixed-coin reward.");
            }

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

        private static bool IsWaypointUnique(string waypoint, IReadOnlyDictionary<string, int> counts) {
            return !string.IsNullOrWhiteSpace(waypoint) &&
                   counts != null &&
                   counts.TryGetValue(waypoint.Trim(), out int count) &&
                   count == 1;
        }

        private static void Assert(bool condition, string message) {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
