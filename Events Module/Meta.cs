using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Modules.Managers;
using Events_Module.Properties;
using Humanizer;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;

namespace Events_Module {

    [JsonObject]
    public class Meta {

        private static readonly Logger Logger = Logger.GetLogger<Meta>();

        private static readonly object IconTextureCacheLock = new object();
        private static readonly Dictionary<string, AsyncTexture2D> IconTextureCache =
            new Dictionary<string, AsyncTexture2D>(StringComparer.OrdinalIgnoreCase);
        private static ContentsManager _contentsManager;
        private static Texture2D _fallbackTexture;

        [JsonObject]
        public struct Phase {
            public string Name     { get; set; }
            public int    Duration { get; set; }
        }

        public event EventHandler<EventArgs> OnNextRunTimeChanged;

        public static List<Meta> Events = new List<Meta>();

        public string   Name       { get; set; }
        public string   Colloquial { get; set; }
        public string   Category   { get; set; }
        public DateTime Offset     { get; set; }
        public string   Difficulty { get; set; }
        public string   Location   { get; set; }
        public string   Waypoint   { get; set; }

        [JsonIgnore]
        public string StableId { get; internal set; }

        [JsonIgnore]
        public string EnglishName { get; internal set; }

        [JsonIgnore]
        internal EventRewardSummary Reward { get; set; }

        public string Wiki
        {
            get => _wikiEn;
            set => _wikiEn = value;
        }

        public int?     Duration   { get; set; }

        [JsonProperty(PropertyName = "Alert")]
        public int? Reminder { get; set; }

        [JsonProperty(PropertyName = "Repeat")]
        public TimeSpan? RepeatInterval { get; set; }

        protected List<DateTime>          _times = new List<DateTime>();
        public    IReadOnlyList<DateTime> Times => _times;

        public Phase[] Phases { get; set; }

        private DateTime _nextTime;
        public DateTime NextTime {
            get => _nextTime;
            protected set {
                if (_nextTime == value) return;

                _nextTime = value;

                this.OnNextRunTimeChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        [JsonIgnore]
        public bool IsWatched = false;

        [JsonIgnore]
        protected bool HasAlerted = false;

        private string _icon;
        private string _localIconPath;
        private string _wikiEn;
        private AsyncTexture2D _texture;

        public Meta() {
            _texture = new AsyncTexture2D(GetFallbackTexture());
        }

        public string Icon {
            get => _icon;
            set {
                if (_icon == value) return;

                _icon = value;
                LoadIconTexture();
            }
        }

        [JsonIgnore]
        internal string LocalIconPath {
            get => _localIconPath;
            set {
                if (string.Equals(_localIconPath, value, StringComparison.OrdinalIgnoreCase)) return;

                _localIconPath = value;
                LoadIconTexture();
            }
        }

        [JsonIgnore]
        public AsyncTexture2D Texture => _texture;

        internal static void ConfigureIconTextures(ContentsManager contentsManager, Texture2D fallbackTexture) {
            lock (IconTextureCacheLock) {
                _contentsManager = contentsManager;
                _fallbackTexture = fallbackTexture;
                IconTextureCache.Clear();
            }
        }

        private static Texture2D GetFallbackTexture() {
            return _fallbackTexture ?? GameService.Content.GetTexture(@"102377");
        }

        private void LoadIconTexture() {
            bool useLocalIcon = !string.IsNullOrWhiteSpace(_localIconPath);

            if (!useLocalIcon && string.IsNullOrWhiteSpace(_icon)) {
                _texture = new AsyncTexture2D(GetFallbackTexture());
                return;
            }

            string cacheKey = useLocalIcon ? "local:" + _localIconPath : "render:" + _icon;

            lock (IconTextureCacheLock) {
                if (IconTextureCache.TryGetValue(cacheKey, out AsyncTexture2D cachedTexture)) {
                    _texture = cachedTexture;
                    return;
                }

                var displayTexture = new AsyncTexture2D(GetFallbackTexture());
                IconTextureCache.Add(cacheKey, displayTexture);
                _texture = displayTexture;

                if (useLocalIcon) {
                    try {
                        Texture2D localTexture = _contentsManager?.GetTexture(_localIconPath);

                        if (localTexture != null && !ReferenceEquals(localTexture, ContentService.Textures.Error)) {
                            displayTexture.SwapTexture(localTexture);
                        } else {
                            Logger.Warn("Failed to load local event icon {eventIcon}.", _localIconPath);
                        }
                    } catch (Exception exception) {
                        Logger.Warn(exception, "Failed to load local event icon {eventIcon}.", _localIconPath);
                    }

                    return;
                }

                try {
                    AsyncTexture2D requestedTexture = GameService.Content.GetRenderServiceTexture(_icon);
                    EventHandler<ValueChangedEventArgs<Texture2D>> textureSwapped = null;

                    textureSwapped = delegate(object sender, ValueChangedEventArgs<Texture2D> args) {
                        requestedTexture.TextureSwapped -= textureSwapped;

                        if (args.NewValue != null && !ReferenceEquals(args.NewValue, ContentService.Textures.Error)) {
                            displayTexture.SwapTexture(args.NewValue);
                        }
                    };

                    requestedTexture.TextureSwapped += textureSwapped;
                } catch (ArgumentException exception) {
                    Logger.Warn(exception, "Ignored invalid event icon {eventIcon}.", _icon);
                }
            }
        }

        public static void UpdateEventSchedules() {
            if (Events == null) return;

            var tsNow = DateTime.Now.ToLocalTime().TimeOfDay;

            foreach (var e in Events) {
                if (e.Times.Count == 0) continue;

                TimeSpan[] justTimes = e.Times.Select(time => time.ToLocalTime().TimeOfDay).OrderBy(time => time.TotalSeconds).ToArray();
                var nextTime = justTimes.FirstOrDefault(ts => ts.TotalSeconds >= tsNow.TotalSeconds);

                if (nextTime.Ticks == 0) { // Timespan default is Ticks == 0
                    e.NextTime = DateTime.Today.AddDays(1) + justTimes[0];
                } else {
                    e.NextTime = DateTime.Today + nextTime;
                }

                double timeUntil = (e.NextTime - DateTime.Now).TotalMinutes;
                if (timeUntil < (e.Reminder ?? -1) && e.IsWatched) {
                    if (!e.HasAlerted && EventsModule.ModuleInstance.NotificationsEnabled) {
                        EventNotification.ShowNotification(
                            Resources.ResourceManager.GetString(e.Name) ?? e.Name,
                            e.Texture,
                            string.Format(Resources.Starts_in__0_, timeUntil.Minutes().Humanize()),
                            10f,
                            e
                        );
                        e.HasAlerted = true;
                    }
                } else {
                    e.HasAlerted = false;
                }
            }
        }

        public static async Task Load(ContentsManager cm) {
            SetEvents(await LoadBundled(cm, EventRewardCatalog.Empty));
        }

        public static Task<List<Meta>> LoadBundled(ContentsManager cm) {
            return LoadBundled(cm, EventRewardCatalog.Empty);
        }

        internal static async Task<List<Meta>> LoadBundled(ContentsManager cm, EventRewardCatalog rewardCatalog) {
            List<Meta> metas = null;

            try {
                using (var eventsReader = new StreamReader(cm.GetFileStream(@"events.json"))) {
                    metas = JsonConvert.DeserializeObject<List<Meta>>(await eventsReader.ReadToEndAsync());
                }
            } catch (Exception e) {
                Logger.Error(e, Resources.Failed_to_load_metas_from_events_json_);
            }

            if (metas == null) {
                return new List<Meta>();
            }

            var uniqueEvents = new List<Meta>();

            foreach (var meta in metas) {
                meta.StableId = "local:" + meta.Category + ":" + meta.Name;
                meta._times.Add(meta.Offset);

                if (meta.RepeatInterval != null && meta.RepeatInterval.Value.TotalSeconds > 0) {
                    // Subtract the repeat interval to ensure that the start time isn't included twice
                    double dailyMinutes = 60 * 24 - meta.RepeatInterval.Value.TotalMinutes;
                    var lastTime = meta.Offset;

                    while (dailyMinutes > 0) {
                        var intervalTime = lastTime.Add(meta.RepeatInterval.Value);

                        meta._times.Add(intervalTime);

                        lastTime = intervalTime;

                        dailyMinutes -= meta.RepeatInterval.Value.TotalMinutes;
                    }
                }

                var rootEvent = uniqueEvents.Find(m => m.Name == meta.Name && m.Category == meta.Category);

                if (rootEvent != null) {
                    rootEvent._times.AddRange(meta.Times);
                } else {
                    uniqueEvents.Add(meta);
                }
            }

            Dictionary<string, int> waypointCounts = CountWaypoints(uniqueEvents.Select(meta => meta.Waypoint));

            foreach (var meta in uniqueEvents) {
                meta._times = meta._times.Distinct().OrderBy(time => time.TimeOfDay).ToList();
                meta.EnglishName = meta.Name;
                meta.Reward = (rewardCatalog ?? EventRewardCatalog.Empty).Match(
                    meta.StableId,
                    meta.Waypoint,
                    IsWaypointUnique(meta.Waypoint, waypointCounts),
                    meta.Name,
                    meta.Colloquial
                );
            }

            Logger.Info(@"Loaded {eventCount} bundled events.", uniqueEvents.Count);
            return uniqueEvents;
        }

        public static void SetEvents(IEnumerable<Meta> events) {
            Events = events?.Where(meta => meta != null && meta.Times.Count > 0).ToList() ?? new List<Meta>();
            Logger.Info(@"Loaded {eventCount} events.", Events.Count);
            UpdateEventSchedules();
        }

        internal static List<Meta> CreateOfficialEvents(IEnumerable<OfficialEventDefinition> definitions,
                                                        IReadOnlyList<Meta> bundledEvents,
                                                        EventRewardCatalog rewardCatalog = null) {
            var bundled = bundledEvents ?? new List<Meta>();
            var officialEvents = new List<Meta>();
            var officialDefinitions = (definitions ?? Enumerable.Empty<OfficialEventDefinition>()).ToList();
            Dictionary<string, int> waypointCounts = CountWaypoints(
                officialDefinitions.Select(definition => definition.Waypoint)
            );
            var iconCandidates = bundled.Select(item => new OfficialEventIconCandidate {
                Name = item.Name,
                Colloquial = item.Colloquial,
                Category = item.Category,
                Location = item.Location,
                Waypoint = item.Waypoint,
                Icon = item.Icon
            }).ToList();
            DateTime utcDay = DateTime.UtcNow.Date;

            foreach (var definition in officialDefinitions) {
                if (definition.StartMinutesUtc == null || definition.StartMinutesUtc.Count == 0) continue;

                Meta template = FindBundledTemplate(definition, bundled);
                string renderIcon = !string.IsNullOrWhiteSpace(template?.Icon) ? template.Icon : null;
                string localIconPath = null;

                if (string.IsNullOrWhiteSpace(renderIcon)) {
                    localIconPath = OfficialEventIconMatcher.FindLocalIconPath(definition.StableId);

                    if (string.IsNullOrWhiteSpace(localIconPath)) {
                        renderIcon = OfficialEventIconMatcher.FindBestIcon(definition, iconCandidates);
                    }
                }

                var meta = new Meta {
                    StableId = definition.StableId,
                    Name = template?.Name ?? definition.Name,
                    EnglishName = definition.Name,
                    Colloquial = template?.Colloquial,
                    Category = template?.Category ?? definition.Category,
                    Difficulty = template?.Difficulty,
                    Location = template?.Location ?? definition.GroupName,
                    Waypoint = definition.Waypoint,
                    Wiki = definition.Wiki,
                    Duration = definition.Duration > 0 ? (int?)definition.Duration : null,
                    Reminder = template?.Reminder ?? 5,
                    Phases = template?.Phases,
                    Icon = renderIcon,
                    LocalIconPath = localIconPath
                };

                meta.Reward = (rewardCatalog ?? EventRewardCatalog.Empty).Match(
                    definition.StableId,
                    definition.Waypoint,
                    IsWaypointUnique(definition.Waypoint, waypointCounts),
                    definition.Name,
                    template?.Name,
                    template?.Colloquial
                );

                meta._times.AddRange(definition.StartMinutesUtc
                                               .Where(minute => minute >= 0 && minute < 24 * 60)
                                               .Distinct()
                                               .OrderBy(minute => minute)
                                               .Select(minute => DateTime.SpecifyKind(utcDay.AddMinutes(minute), DateTimeKind.Utc)));

                if (meta._times.Count > 0) officialEvents.Add(meta);
            }

            return officialEvents;
        }

        private static Dictionary<string, int> CountWaypoints(IEnumerable<string> waypoints) {
            return (waypoints ?? Enumerable.Empty<string>())
                   .Where(waypoint => !string.IsNullOrWhiteSpace(waypoint))
                   .Select(waypoint => waypoint.Trim())
                   .GroupBy(waypoint => waypoint, StringComparer.Ordinal)
                   .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        }

        private static bool IsWaypointUnique(string waypoint, IReadOnlyDictionary<string, int> counts) {
            return !string.IsNullOrWhiteSpace(waypoint) &&
                   counts != null &&
                   counts.TryGetValue(waypoint.Trim(), out int count) &&
                   count == 1;
        }

        private static Meta FindBundledTemplate(OfficialEventDefinition definition, IReadOnlyList<Meta> bundled) {
            Meta bestMatch = null;
            int bestScore = 0;

            foreach (Meta candidate in bundled) {
                int score = 0;

                if (string.Equals(candidate.Name, definition.Name, StringComparison.OrdinalIgnoreCase)) score += 100;
                if (string.Equals(candidate.Colloquial, definition.Name, StringComparison.OrdinalIgnoreCase)) score += 90;
                if (string.Equals(candidate.Location, definition.Name, StringComparison.OrdinalIgnoreCase)) score += 70;
                if (string.Equals(candidate.Location, definition.GroupName, StringComparison.OrdinalIgnoreCase)) score += 50;
                if (!string.IsNullOrWhiteSpace(definition.GroupName) &&
                    candidate.Name?.IndexOf(definition.GroupName, StringComparison.OrdinalIgnoreCase) >= 0) score += 40;
                if (!string.IsNullOrWhiteSpace(candidate.Wiki) &&
                    string.Equals(candidate.Wiki, definition.Wiki, StringComparison.OrdinalIgnoreCase)) score += 20;

                if (score > bestScore) {
                    bestScore = score;
                    bestMatch = candidate;
                }
            }

            return bestScore >= 70 ? bestMatch : null;
        }
    }

}
