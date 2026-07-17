using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using Events_Module.Properties;
using Humanizer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Events_Module {

    [Export(typeof(Module))]
    public class EventsModule : Module {

        private static readonly Logger Logger = Logger.GetLogger<EventsModule>();

        internal static EventsModule ModuleInstance;

        // Service Managers
        internal SettingsManager    SettingsManager    => this.ModuleParameters.SettingsManager;
        internal ContentsManager    ContentsManager    => this.ModuleParameters.ContentsManager;
        internal DirectoriesManager DirectoriesManager => this.ModuleParameters.DirectoriesManager;
        internal Gw2ApiManager      Gw2ApiManager      => this.ModuleParameters.Gw2ApiManager;

        private string _ddAlphabetical = Resources.Alphabetical;
        private string _ddNextup = Resources.Next_Up;

        private string _ecAllevents     = Resources.All_Events;
        private string _ecWatchedEvents = Resources.Watched_Events;
        private string _ecHidden        = Resources.Hidden_Events;

        private const int TIMER_RECALC_RATE = 5;
        private static readonly TimeSpan OfficialRefreshInterval = TimeSpan.FromHours(6);
        private const string RareRewardIcon = "EF63A10BD2317CECCEA63A3B7E6555550B414C4E/1766399";
        private const string DragoniteRewardIcon = "D53E69EFB3AFF4C85CC370AA32F1A6A61C03CCE8/631482";

        private List<DetailsButton> _displayedEvents;
        private readonly Dictionary<Meta, EventHandler<EventArgs>> _scheduleHandlers = new Dictionary<Meta, EventHandler<EventArgs>>();
        private readonly Dictionary<DetailsButton, Meta> _eventMetaByButton = new Dictionary<DetailsButton, Meta>();

        private WindowTab _eventsTab;

        private Panel _tabPanel;

        private SettingCollection  _watchCollection;
        private SettingEntry<bool> _settingNotificationsEnabled;
        private SettingEntry<bool> _settingChimeEnabled;

        private SettingEntry<Point> _settingNotificationsPosition;

        private Texture2D _textureWatch;
        private Texture2D _textureWatchActive;
        private AsyncTexture2D _textureRareReward;
        private AsyncTexture2D _textureDragoniteReward;

        private IReadOnlyList<Meta> _bundledEvents = new List<Meta>();
        private EventRewardCatalog _rewardCatalog = EventRewardCatalog.Empty;
        private OfficialEventTimerService _officialEventTimerService;
        private CancellationTokenSource _officialRefreshCancellation;
        private Task<OfficialEventTimerSourceResult> _officialRefreshTask;
        private DateTime _nextOfficialRefreshUtc = DateTime.MaxValue;
        private long _officialRevisionId;
        private string _officialSha1;
        private bool _usingBundledEvents = true;

        public event EventHandler SourceStatusChanged;

        public string SourceStatus { get; private set; } = string.Empty;

        public bool NotificationsEnabled {
            get => _settingNotificationsEnabled.Value;
            set => _settingNotificationsEnabled.Value = value;
        }

        public bool ChimeEnabled {
            get => _settingChimeEnabled.Value;
            set => _settingChimeEnabled.Value = value;
        }

        public Point NotificationPosition {
            get => _settingNotificationsPosition.Value;
            set => _settingNotificationsPosition.Value = value;
        }

        [ImportingConstructor]
        public EventsModule([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters) {
            ModuleInstance = this;
        }

        protected override void DefineSettings(SettingCollection settings) {
            var selfManagedSettings = settings.AddSubCollection(@"Managed Settings");

            _settingNotificationsEnabled = selfManagedSettings.DefineSetting(@"notificationsEnabled", true);
            _settingChimeEnabled         = selfManagedSettings.DefineSetting(@"chimeEnabled",         true);

            _settingNotificationsPosition = selfManagedSettings.DefineSetting("notificationPosition", new Point(180, 60));

            _watchCollection = settings.AddSubCollection(@"Watching");
        }

        protected override void Initialize() {
            _displayedEvents = new List<DetailsButton>();
            GameService.Overlay.UserLocaleChanged += ChangeLocalization;
        }

        private void LoadTextures() {
            _textureWatch       = ContentsManager.GetTexture(@"textures\605021.png");
            _textureWatchActive = ContentsManager.GetTexture(@"textures\605019.png");
            _textureRareReward = GameService.Content.GetRenderServiceTexture(RareRewardIcon);
            _textureDragoniteReward = GameService.Content.GetRenderServiceTexture(DragoniteRewardIcon);
            Meta.ConfigureIconTextures(ContentsManager, ContentsManager.GetTexture(@"textures\1466345.png"));
        }

        protected override async Task LoadAsync() {
            LoadTextures();

            _rewardCatalog = await LoadRewardCatalogAsync();
            _bundledEvents = await Meta.LoadBundled(this.ContentsManager, _rewardCatalog);
            Meta.SetEvents(_bundledEvents);

            _officialRefreshCancellation = new CancellationTokenSource();

            try {
                string cacheDirectory = DirectoriesManager.GetFullDirectoryPath("events-cache");
                _officialEventTimerService = new OfficialEventTimerService(cacheDirectory);
                OfficialEventTimerSourceResult officialResult = await _officialEventTimerService.RefreshAsync(
                    force: false,
                    cancellationToken: _officialRefreshCancellation.Token
                );
                ApplyOfficialResult(officialResult);
            } catch (Exception exception) {
                Logger.Warn(exception, "Failed to initialize the official Guild Wars 2 Wiki event timer source.");
                UseBundledStatus(exception.Message);
            }

            _nextOfficialRefreshUtc = DateTime.UtcNow + OfficialRefreshInterval;

            _tabPanel = BuildSettingPanel(GameService.Overlay.BlishHudWindow.ContentRegion);
        }

        protected override void OnModuleLoaded(EventArgs e) {
            _eventsTab = GameService.Overlay.BlishHudWindow.AddTab(Resources.Events_and_Metas, this.ContentsManager.GetTexture(@"textures\1466345.png"), _tabPanel);

            base.OnModuleLoaded(e);
        }

        public override IView GetSettingsView() {
            return new BasicSettingsView();
        }

        internal void ShowSetNotificationPositions() {
            var tempSizeSetting = new SettingEntry<Point>() {
                Value = new Point(280, 512)
            };

            var choseLocation = new NotificationMover(new ScreenRegion("Notifications", _settingNotificationsPosition, tempSizeSetting));
            choseLocation.Parent = GameService.Graphics.SpriteScreen;
            choseLocation.Size = GameService.Graphics.SpriteScreen.ContentRegion.Size;
        }

        internal void RequestOfficialRefresh() {
            StartOfficialRefresh(force: true);
        }

        internal static string Localize(string key, string fallback) {
            return Resources.ResourceManager.GetString(key) ?? fallback;
        }

        private Panel BuildSettingPanel(Rectangle panelBounds) {
            _eventMetaByButton.Clear();

            var etPanel = new Panel() {
                CanScroll = false,
                Size = panelBounds.Size
            };

            var ddSortMethod = new Dropdown() {
                Location = new Point(etPanel.Right - 150 - Dropdown.Standard.ControlOffset.X, Dropdown.Standard.ControlOffset.Y),
                Width    = 150,
                Parent   = etPanel,
            };

            var notificationToggle = new Checkbox() {
                Text     = Resources.Enable_Notifications,
                Checked  = this.NotificationsEnabled,
                Parent   = etPanel
            };

            notificationToggle.Location = new Point(ddSortMethod.Left - notificationToggle.Width - 10, ddSortMethod.Top + 6);

            var chimeToggle = new Checkbox {
                Text    = Resources.Mute_Notifications,
                Checked = !this.ChimeEnabled,
                Parent  = etPanel,
                Top     = notificationToggle.Top,
                Right   = notificationToggle.Left - 10
            };

            notificationToggle.CheckedChanged += delegate (object sender, CheckChangedEvent e) { this.NotificationsEnabled = e.Checked; };
            chimeToggle.CheckedChanged        += delegate (object sender, CheckChangedEvent e) { this.ChimeEnabled         = !e.Checked; };

            int topOffset = ddSortMethod.Bottom + Panel.MenuStandard.ControlOffset.Y;

            var menuSection = new Panel {
                Title      = Resources.Event_Categories,
                ShowBorder = true,
                Size       = Panel.MenuStandard.Size - new Point(0, topOffset + Panel.MenuStandard.ControlOffset.Y),
                Location   = new Point(Panel.MenuStandard.PanelOffset.X, topOffset),
                Parent     = etPanel
            };

            var eventPanel = new FlowPanel() {
                FlowDirection  = ControlFlowDirection.LeftToRight,
                ControlPadding = new Vector2(8, 8),
                Location       = new Point(menuSection.Right + Panel.MenuStandard.ControlOffset.X, menuSection.Top),
                Size           = new Point(ddSortMethod.Right - menuSection.Right - Control.ControlStandard.ControlOffset.X, menuSection.Height),
                CanScroll      = true,
                Parent         = etPanel
            };

            var searchBox = new TextBox() {
                PlaceholderText = Resources.Event_Search,
                Width           = menuSection.Width,
                Location        = new Point(ddSortMethod.Top, menuSection.Left),
                Parent          = etPanel
            };

            searchBox.TextChanged += delegate (object sender, EventArgs args) {
                string query = searchBox.Text;
                eventPanel.FilterChildren<DetailsButton>(db =>
                    _eventMetaByButton.TryGetValue(db, out Meta eventMeta) && EventMatchesSearch(eventMeta, query));
            };

            //eventPanel.SuspendLayout();

            foreach (var meta in Meta.Events) {
                var legacySetting = _watchCollection.DefineSetting(@"watchEvent:" + meta.Name, true);
                string stableId = string.IsNullOrWhiteSpace(meta.StableId)
                    ? "local:" + meta.Category + ":" + meta.Name
                    : meta.StableId;
                var setting = _watchCollection.DefineSetting(@"watchEventId:" + stableId, legacySetting.Value);

                meta.IsWatched = setting.Value;

                var es2 = new DetailsButton {
                    Parent           = eventPanel,
                    BasicTooltipText = Resources.ResourceManager.GetString(meta.Category) ?? meta.Category,
                    Text             = GetEventDisplayText(meta),
                    IconSize         = DetailsIconSize.Small,
                    ShowVignette     = false,
                    HighlightType    = DetailsHighlightType.LightHighlight,
                    ShowToggleButton = true
                };

                _eventMetaByButton.Add(es2, meta);

                if (meta.Texture != null && meta.Texture.HasTexture) {
                    es2.Icon = meta.Texture;
                }

                var nextTimeLabel = new Label() {
                    Size                = new Point(65, es2.ContentRegion.Height),
                    Text                = meta.NextTime.ToShortTimeString(),
                    BasicTooltipText    = GetTimeDetails(meta),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Middle,
                    Parent              = es2,
                };

                if (meta.Reward != null) {
                    new EventRewardSummaryControl(meta.Reward, _textureRareReward, _textureDragoniteReward) {
                        BasicTooltipText = GetRewardDetails(meta.Reward),
                        Parent = es2
                    };
                }

                if (!string.IsNullOrEmpty(meta.Wiki)) {
                    var glowWikiBttn = new GlowButton {
                        Icon = GameService.Content.GetTexture(@"102530"),
                        ActiveIcon = GameService.Content.GetTexture(@"glow-wiki"),
                        BasicTooltipText = Resources.Read_about_this_event_on_the_wiki_,
                        Parent = es2,
                        GlowColor = Color.White * 0.1f
                    };

                    glowWikiBttn.Click += delegate {
                        if (UrlIsValid(meta.Wiki)) {
                            Process.Start(meta.Wiki);
                        }
                    };
                }

                if (!string.IsNullOrEmpty(meta.Waypoint)) {
                    var glowWaypointBttn = new GlowButton {
                        Icon = GameService.Content.GetTexture(@"waypoint"),
                        ActiveIcon = GameService.Content.GetTexture(@"glow-waypoint"),
                        BasicTooltipText = string.Format(Resources.Nearby_waypoint___0_, meta.Waypoint),
                        Parent = es2,
                        GlowColor = Color.White * 0.1f
                    };

                    glowWaypointBttn.Click += delegate {
                        ClipboardUtil.WindowsClipboardService.SetTextAsync(meta.Waypoint)
                                     .ContinueWith((clipboardResult) => {
                                           if (clipboardResult.IsFaulted) {
                                               ScreenNotification.ShowNotification(Resources.Failed_to_copy_waypoint_to_clipboard__Try_again_, ScreenNotification.NotificationType.Red, duration: 2);
                                           } else {
                                               ScreenNotification.ShowNotification(Resources.Copied_waypoint_to_clipboard_, duration: 2);
                                           }
                                       });
                    };
                }

                //eventPanel.ResumeLayout(true);

                var toggleFollowBttn = new GlowButton() {
                    Icon             = _textureWatch,
                    ActiveIcon       = _textureWatchActive,
                    BasicTooltipText = Resources.Click_to_toggle_tracking_for_this_event_,
                    ToggleGlow       = true,
                    Checked          = meta.IsWatched,
                    Parent           = es2,
                };

                toggleFollowBttn.Click += delegate {
                    meta.IsWatched = toggleFollowBttn.Checked;
                    setting.Value  = toggleFollowBttn.Checked;
                };

                EventHandler<EventArgs> scheduleChanged = delegate {
                    UpdateSort(ddSortMethod, EventArgs.Empty);
                    SortEventPanel(ddSortMethod.SelectedItem, ref eventPanel);

                    nextTimeLabel.Text             = meta.NextTime.ToShortTimeString();
                    nextTimeLabel.BasicTooltipText = GetTimeDetails(meta);
                };
                meta.OnNextRunTimeChanged += scheduleChanged;
                _scheduleHandlers[meta] = scheduleChanged;

                _displayedEvents.Add(es2);
            }

            // Add menu items for each category (and built-in categories)
            var eventCategories = new Menu {
                Size           = menuSection.ContentRegion.Size,
                MenuItemHeight = 40,
                Parent         = menuSection,
                CanSelect      = true
            };

            List<IGrouping<string, Meta>> submetas = Meta.Events.GroupBy(e => e.Category).ToList();

            var evAll = eventCategories.AddMenuItem(_ecAllevents);
            evAll.Select();
            evAll.Click += delegate {
                eventPanel.FilterChildren<DetailsButton>(db => true);
            };

            var evWatched = eventCategories.AddMenuItem(_ecWatchedEvents);
            evWatched.Click += delegate {
                eventPanel.FilterChildren<DetailsButton>(db => {
                    Meta watchedMeta;
                    return _eventMetaByButton.TryGetValue(db, out watchedMeta) && watchedMeta.IsWatched;
                });
            };

            foreach (IGrouping<string, Meta> e in submetas) {
                var category = Resources.ResourceManager.GetString(e.Key) ?? e.Key;
                var ev = eventCategories.AddMenuItem(category);
                ev.Click += delegate {
                    eventPanel.FilterChildren<DetailsButton>(db => string.Equals(db.BasicTooltipText, category));
                };
            }

            // TODO: Hidden events/timers to be added later
            //eventCategories.AddMenuItem(EC_HIDDEN);

            // Add dropdown for sorting events
            ddSortMethod.Items.Add(_ddAlphabetical);
            ddSortMethod.Items.Add(_ddNextup);

            ddSortMethod.ValueChanged += delegate (object sender, ValueChangedEventArgs args) {
                SortEventPanel(args.CurrentValue, ref eventPanel);
            };

            ddSortMethod.SelectedItem = _ddNextup;
            UpdateSort(ddSortMethod, EventArgs.Empty);

            return etPanel;
        }

        private void RepositionES() {
            int pos = 0;
            foreach (var es in _displayedEvents) {
                int x = pos % 2;
                int y = pos / 2;

                es.Location = new Point(x * 308, y * 108);

                if (es.Visible) pos++;

                if (es.Parent != null) {
                    // TODO: Just expose the panel to the module so that we don't have to do it this dumb way:
                    ((Panel)es.Parent).VerticalScrollOffset = 0;
                    es.Parent.Invalidate();
                }
            }
        }

        private string GetTimeDetails(Meta assignedMeta) {
            var timeUntil = assignedMeta.NextTime - DateTime.Now;

            var msg = new StringBuilder();

            msg.AppendLine(string.Format(Resources.Starts_in__0_,
                                         timeUntil.Humanize(maxUnit: Humanizer.Localisation.TimeUnit.Hour,
                                                            minUnit: Humanizer.Localisation.TimeUnit.Minute,
                                                            precision: 2,
                                                            collectionSeparator: null))
            );

            msg.Append(Environment.NewLine + Resources.Upcoming_Event_Times_);
            foreach (var utime in assignedMeta.Times.Select(time => {
                DateTime upcoming = DateTime.Today + time.ToLocalTime().TimeOfDay;
                return upcoming >= DateTime.Now ? upcoming : upcoming + 1.Days();
            }).OrderBy(time => time.Ticks).ToList()) {
                msg.Append(Environment.NewLine + utime.ToShortTimeString());
            }

            return msg.ToString();
        }

        private static string GetLocalizedEventName(Meta meta) {
            return Resources.ResourceManager.GetString(meta.Name) ?? meta.Name;
        }

        private static string GetEventDisplayText(Meta meta) {
            string localizedName = GetLocalizedEventName(meta);
            string englishName = string.IsNullOrWhiteSpace(meta.EnglishName) ? meta.Name : meta.EnglishName;

            if (string.IsNullOrWhiteSpace(englishName) ||
                string.Equals(localizedName, englishName, StringComparison.CurrentCultureIgnoreCase)) {
                return localizedName;
            }

            return localizedName + Environment.NewLine + englishName;
        }

        private static bool EventMatchesSearch(Meta meta, string query) {
            if (string.IsNullOrWhiteSpace(query)) return true;

            string[] candidates = {
                GetLocalizedEventName(meta),
                meta.EnglishName,
                meta.Name,
                meta.Colloquial
            };

            return candidates.Any(candidate =>
                !string.IsNullOrWhiteSpace(candidate) &&
                candidate.IndexOf(query.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        private static string GetRewardDetails(EventRewardSummary reward) {
            var message = new StringBuilder();
            message.AppendLine(Localize("Reward_information", "World boss rewards"));
            message.AppendLine(string.Format(
                Localize("Reward_guaranteed_rare_or_exotic_items", "Guaranteed rare or exotic items: at least {0}"),
                reward.MinimumRareOrExoticItems
            ));
            message.AppendLine(Localize("Reward_bonus_chest_daily_limit", "Bonus chest: once per account per day."));
            message.AppendLine();
            message.AppendLine(string.Format(
                Localize("Reward_guaranteed_ground_chest_dragonite", "Guaranteed Dragonite Ore in the ground chest: {0}"),
                reward.DragoniteAmount
            ));
            message.AppendLine(Localize("Reward_ground_chest_daily_limit", "Ground chest: once per character per day."));

            if (!string.IsNullOrWhiteSpace(reward.NoteKey)) {
                message.AppendLine(Localize(reward.NoteKey, reward.NoteKey));
            }

            message.AppendLine();
            message.AppendLine(string.Format(
                Localize("Reward_source", "Source: Guild Wars 2 Wiki — {0}"),
                reward.SourceName
            ));
            message.Append(string.Format(
                Localize("Reward_verified_on", "Verified: {0:yyyy-MM-dd}"),
                reward.VerifiedOn
            ));
            return message.ToString();
        }

        private void UpdateSort(object sender, EventArgs e) {
            var item = ((Dropdown)sender).SelectedItem;
            if (item == _ddAlphabetical) {
                _displayedEvents.Sort(CompareEventButtonsAlphabetically);
            } else if (item == _ddNextup) {
                _displayedEvents.Sort(CompareEventButtonsByNextTime);
            }

            RepositionES();
        }

        private void SortEventPanel(string ddSortMethodValue, ref FlowPanel eventPanel) {
            if (ddSortMethodValue == _ddAlphabetical) {
                eventPanel.SortChildren<DetailsButton>(CompareEventButtonsAlphabetically);
            } else if (ddSortMethodValue == _ddNextup) {
                eventPanel.SortChildren<DetailsButton>(CompareEventButtonsByNextTime);
            }
        }

        private int CompareEventButtonsAlphabetically(DetailsButton left, DetailsButton right) {
            if (!_eventMetaByButton.TryGetValue(left, out Meta leftMeta)) return 1;
            if (!_eventMetaByButton.TryGetValue(right, out Meta rightMeta)) return -1;

            int localizedComparison = string.Compare(
                GetLocalizedEventName(leftMeta),
                GetLocalizedEventName(rightMeta),
                StringComparison.CurrentCultureIgnoreCase
            );
            if (localizedComparison != 0) return localizedComparison;

            int englishComparison = string.Compare(
                leftMeta.EnglishName ?? leftMeta.Name,
                rightMeta.EnglishName ?? rightMeta.Name,
                StringComparison.CurrentCultureIgnoreCase
            );
            return englishComparison != 0
                ? englishComparison
                : string.Compare(leftMeta.StableId, rightMeta.StableId, StringComparison.Ordinal);
        }

        private int CompareEventButtonsByNextTime(DetailsButton left, DetailsButton right) {
            if (!_eventMetaByButton.TryGetValue(left, out Meta leftMeta)) return 1;
            if (!_eventMetaByButton.TryGetValue(right, out Meta rightMeta)) return -1;

            int timeComparison = leftMeta.NextTime.CompareTo(rightMeta.NextTime);
            return timeComparison != 0 ? timeComparison : CompareEventButtonsAlphabetically(left, right);
        }

        // Utility
        private static bool UrlIsValid(string source) => Uri.TryCreate(source, UriKind.Absolute, out Uri uriResult) && uriResult.Scheme == Uri.UriSchemeHttps;

        private double _elapsedSeconds = 0;

        protected override void Update(GameTime gameTime) {
            CompleteOfficialRefreshIfReady();

            _elapsedSeconds += gameTime.ElapsedGameTime.TotalSeconds;

            if (_elapsedSeconds > TIMER_RECALC_RATE) {
                Meta.UpdateEventSchedules();

                if (DateTime.UtcNow >= _nextOfficialRefreshUtc) {
                    StartOfficialRefresh(force: false);
                }

                _elapsedSeconds = 0;
            }
        }

        protected override void Unload() {
            CancellationTokenSource cancellation = _officialRefreshCancellation;
            OfficialEventTimerService service = _officialEventTimerService;
            Task<OfficialEventTimerSourceResult> refreshTask = _officialRefreshTask;

            cancellation?.Cancel();
            if (refreshTask != null && !refreshTask.IsCompleted) {
                refreshTask.ContinueWith(completed => {
                    if (completed.IsFaulted) {
                        Logger.Warn(completed.Exception, "The official event timer refresh ended during module unload.");
                    }
                    service?.Dispose();
                    cancellation?.Dispose();
                }, TaskScheduler.Default);
            } else {
                if (refreshTask?.IsFaulted == true) {
                    Logger.Warn(refreshTask.Exception, "The official event timer refresh ended during module unload.");
                }
                service?.Dispose();
                cancellation?.Dispose();
            }

            _officialRefreshTask = null;
            _officialEventTimerService = null;
            _officialRefreshCancellation = null;
            _textureRareReward = null;
            _textureDragoniteReward = null;

            GameService.Overlay.UserLocaleChanged -= ChangeLocalization;
            UnsubscribeScheduleHandlers();
            _eventMetaByButton.Clear();
            if (_eventsTab != null) GameService.Overlay.BlishHudWindow.RemoveTab(_eventsTab);

            ModuleInstance = null;
        }

        private void ChangeLocalization(object sender, EventArgs e) {
            _ddAlphabetical = Resources.Alphabetical;
            _ddNextup = Resources.Next_Up;
            _ecAllevents = Resources.All_Events;
            _ecWatchedEvents = Resources.Watched_Events;
            _ecHidden = Resources.Hidden_Events;

            RebuildEventTab();
        }

        private void StartOfficialRefresh(bool force) {
            if (_officialEventTimerService == null || _officialRefreshCancellation == null || _officialRefreshTask != null) return;

            SetSourceStatus(Localize("Official_timer_refreshing", "Checking the official Guild Wars 2 Wiki..."));
            _officialRefreshTask = _officialEventTimerService.RefreshAsync(force, _officialRefreshCancellation.Token);
            _nextOfficialRefreshUtc = DateTime.UtcNow + OfficialRefreshInterval;
        }

        private void CompleteOfficialRefreshIfReady() {
            if (_officialRefreshTask == null || !_officialRefreshTask.IsCompleted) return;

            Task<OfficialEventTimerSourceResult> completedTask = _officialRefreshTask;
            _officialRefreshTask = null;

            try {
                if (completedTask.IsCanceled) return;

                OfficialEventTimerSourceResult result = completedTask.GetAwaiter().GetResult();
                bool dataChanged = ApplyOfficialResult(result);
                if (dataChanged) RebuildEventTab();
            } catch (OperationCanceledException) {
                // Module unload cancels an in-flight request.
            } catch (Exception exception) {
                Logger.Warn(exception, "Failed to apply refreshed Guild Wars 2 Wiki event timer data.");
                if (_usingBundledEvents) UseBundledStatus(exception.Message);
            }
        }

        private bool ApplyOfficialResult(OfficialEventTimerSourceResult result) {
            if (result?.Events == null || result.Events.Count == 0) {
                if (_officialRevisionId == 0) {
                    Meta.SetEvents(_bundledEvents);
                    _usingBundledEvents = true;
                    if (result?.TimedOut == true) {
                        UseBundledTimeoutStatus();
                    } else {
                        UseBundledStatus(result?.Error);
                    }
                }
                return false;
            }

            bool dataChanged = _usingBundledEvents || result.RevisionId != _officialRevisionId ||
                               !string.Equals(result.Sha1, _officialSha1, StringComparison.OrdinalIgnoreCase);

            if (dataChanged) {
                Meta.SetEvents(Meta.CreateOfficialEvents(result.Events, _bundledEvents, _rewardCatalog));
                _officialRevisionId = result.RevisionId;
                _officialSha1 = result.Sha1;
                _usingBundledEvents = false;
            }

            string statusKey = result.Source == OfficialEventTimerSource.OfficialWiki
                ? "Official_timer_source_live"
                : "Official_timer_source_cache";
            string fallback = result.Source == OfficialEventTimerSource.OfficialWiki
                ? "Official Wiki {0} — revision {1} ({2:g}), checked {3:g}, SHA1 {4}"
                : "Last known good official Wiki data {0} — revision {1} ({2:g}), checked {3:g}, SHA1 {4}";
            SetSourceStatus(string.Format(Localize(statusKey, fallback),
                                          result.WidgetVersion,
                                          result.RevisionId,
                                          result.RevisionTimestampUtc.ToLocalTime(),
                                          result.LastCheckedUtc.ToLocalTime(),
                                          result.Sha1));
            return dataChanged;
        }

        private void UseBundledStatus(string error = null) {
            if (string.IsNullOrWhiteSpace(error)) {
                SetSourceStatus(Localize("Official_timer_source_bundled", "Bundled events.json fallback — official Wiki unavailable"));
                return;
            }

            SetSourceStatus(string.Format(
                Localize("Official_timer_source_bundled_error", "Bundled events.json fallback — official Wiki unavailable: {0}"),
                error
            ));
        }

        private void UseBundledTimeoutStatus() {
            SetSourceStatus(string.Format(
                Localize("Official_timer_source_bundled_timeout", "Bundled events.json fallback — official Wiki request exceeded {0} seconds"),
                OfficialEventTimerService.RequestTimeoutSeconds
            ));
        }

        private void SetSourceStatus(string status) {
            if (string.Equals(SourceStatus, status, StringComparison.Ordinal)) return;
            SourceStatus = status;
            SourceStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RebuildEventTab() {
            if (_tabPanel == null) return;

            UnsubscribeScheduleHandlers();
            _tabPanel.Dispose();
            _displayedEvents.Clear();
            _eventMetaByButton.Clear();
            _tabPanel = BuildSettingPanel(GameService.Overlay.BlishHudWindow.ContentRegion);

            if (_eventsTab != null) {
                GameService.Overlay.BlishHudWindow.RemoveTab(_eventsTab);
                _eventsTab = GameService.Overlay.BlishHudWindow.AddTab(
                    Resources.Events_and_Metas,
                    this.ContentsManager.GetTexture(@"textures\1466345.png"),
                    _tabPanel
                );
            }
        }

        private void UnsubscribeScheduleHandlers() {
            foreach (var handler in _scheduleHandlers) {
                handler.Key.OnNextRunTimeChanged -= handler.Value;
            }
            _scheduleHandlers.Clear();
        }

        private async Task<EventRewardCatalog> LoadRewardCatalogAsync() {
            try {
                using (var reader = new StreamReader(ContentsManager.GetFileStream(@"event-rewards.json"))) {
                    EventRewardCatalog catalog = EventRewardCatalog.Parse(await reader.ReadToEndAsync());
                    Logger.Info("Loaded reward information for {rewardEventCount} events.", catalog.Count);
                    return catalog;
                }
            } catch (Exception exception) {
                Logger.Warn(exception, "Failed to load event reward information; event schedules will remain available without rewards.");
                return EventRewardCatalog.Empty;
            }
        }

    }

}
