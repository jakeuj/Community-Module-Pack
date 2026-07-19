using System;
using System.Linq;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Microsoft.Xna.Framework;

namespace Events_Module {

    public class BasicSettingsView : View {

        private const int ContentLeft = 32;
        private const int ContentWidth = 640;

        protected override void Build(Container buildPanel) {
            var contentPanel = new Panel {
                Size = buildPanel.ContentRegion.Size,
                CanScroll = true,
                Parent = buildPanel
            };

            EventsModule module = EventsModule.ModuleInstance;
            int top = 32;

            var formatTitle = new Label {
                Text = EventsModule.Localize("Chat_message_format_title", "Chat message copy format"),
                Location = new Point(ContentLeft, top),
                Size = new Point(ContentWidth, 24),
                Font = Blish_HUD.ContentService.Content.DefaultFont16,
                Parent = contentPanel
            };

            var enableCustomFormat = new Checkbox {
                Text = EventsModule.Localize("Chat_message_format_enable", "Use a custom chat message format"),
                BasicTooltipText = EventsModule.Localize(
                    "Chat_message_format_enable_tooltip",
                    "When enabled, waypoint copy actions include event details and your custom text."
                ),
                Checked = module?.UseCustomCopyFormat ?? false,
                Location = new Point(ContentLeft, formatTitle.Bottom + 8),
                Parent = contentPanel
            };

            var formatInput = new TextBox {
                Text = module?.CustomCopyFormat ?? EventsModule.DefaultChatMessageFormat,
                PlaceholderText = EventsModule.DefaultChatMessageFormat,
                Width = ContentWidth,
                Location = new Point(ContentLeft, enableCustomFormat.Bottom + 8),
                Enabled = enableCustomFormat.Checked,
                Parent = contentPanel
            };

            var supportedFields = new Label {
                Text = EventsModule.Localize(
                    "Chat_message_format_fields",
                    "Fields: {point}, {event}, {event_zh}, {event_en}, {category}, {category_zh}, {category_en}, {time}. Use {{ and }} for literal braces."
                ),
                Location = new Point(ContentLeft, formatInput.Bottom + 6),
                Size = new Point(ContentWidth, 48),
                WrapText = true,
                Parent = contentPanel
            };

            var validationMessage = new Label {
                Location = new Point(ContentLeft, supportedFields.Bottom + 2),
                Size = new Point(ContentWidth, 24),
                TextColor = Color.Red,
                Parent = contentPanel
            };

            var preview = new Label {
                Location = new Point(ContentLeft, validationMessage.Bottom + 2),
                Size = new Point(ContentWidth, 54),
                WrapText = true,
                Parent = contentPanel
            };

            var resetFormat = new StandardButton {
                Text = EventsModule.Localize("Chat_message_format_reset", "Reset format"),
                Width = 160,
                Location = new Point(ContentLeft, preview.Bottom + 8),
                Parent = contentPanel
            };

            Action updateFormatControls = delegate {
                formatInput.Enabled = enableCustomFormat.Checked;

                Meta previewMeta = GetPreviewMeta();
                if (module == null || previewMeta == null) {
                    validationMessage.Text = string.Empty;
                    preview.Text = EventsModule.Localize(
                        "Chat_message_format_preview_unavailable",
                        "Preview unavailable because no event currently has a waypoint."
                    );
                    return;
                }

                EventChatMessageFormatResult result = module.FormatEventChatMessage(previewMeta, formatInput.Text);
                validationMessage.Text = result.IsValid ? string.Empty : GetFormatError(result);

                string previewText = result.IsValid ? result.Text : previewMeta.Waypoint.Trim();
                preview.Text = string.Format(
                    EventsModule.Localize("Chat_message_format_preview", "Format preview: {0}"),
                    previewText
                );
            };

            enableCustomFormat.CheckedChanged += delegate(object sender, CheckChangedEvent e) {
                if (module != null) module.UseCustomCopyFormat = e.Checked;
                updateFormatControls();
            };
            formatInput.TextChanged += delegate {
                if (module != null) module.CustomCopyFormat = formatInput.Text;
                updateFormatControls();
            };
            resetFormat.Click += delegate {
                formatInput.Text = EventsModule.DefaultChatMessageFormat;
                if (module != null) module.CustomCopyFormat = formatInput.Text;
                updateFormatControls();
            };

            updateFormatControls();

            var setPosition = new StandardButton {
                Text = EventsModule.Localize("Set_notification_position", "Set Notification Position"),
                Width = 196,
                Location = new Point(ContentLeft, resetFormat.Bottom + 30),
                Parent = contentPanel
            };

            setPosition.Click += SetPosition_Click;

            var sourceTitle = new Label {
                Text = EventsModule.Localize("Official_timer_data", "Event timer data"),
                Location = new Point(ContentLeft, setPosition.Bottom + 30),
                Size = new Point(ContentWidth, 24),
                Font = Blish_HUD.ContentService.Content.DefaultFont16,
                Parent = contentPanel
            };

            var sourceStatus = new Label {
                Text = module?.SourceStatus ?? string.Empty,
                Location = new Point(ContentLeft, sourceTitle.Bottom + 6),
                Size = new Point(ContentWidth, 66),
                WrapText = true,
                Parent = contentPanel
            };

            var refresh = new StandardButton {
                Text = EventsModule.Localize("Refresh_official_event_timer", "Refresh official event timer"),
                Width = 220,
                Location = new Point(ContentLeft, sourceStatus.Bottom + 8),
                Parent = contentPanel
            };

            refresh.Click += delegate { module?.RequestOfficialRefresh(); };

            if (module != null) {
                var updateTitle = new Label {
                    Text = EventsModule.Localize("Module_update_title", "Module updates"),
                    Location = new Point(ContentLeft, refresh.Bottom + 30),
                    Size = new Point(ContentWidth, 24),
                    Font = Blish_HUD.ContentService.Content.DefaultFont16,
                    Parent = contentPanel
                };

                var autoUpdate = new Checkbox {
                    Text = EventsModule.Localize("Module_auto_update", "Automatically update this module"),
                    BasicTooltipText = EventsModule.Localize(
                        "Module_auto_update_tooltip",
                        "Install stable GitHub releases at the next module load. A successful update restarts Blish HUD immediately."
                    ),
                    Checked = module.AutoUpdateEnabled,
                    Location = new Point(ContentLeft, updateTitle.Bottom + 8),
                    Parent = contentPanel
                };

                var updateVersions = new Label {
                    Location = new Point(ContentLeft, autoUpdate.Bottom + 8),
                    Size = new Point(ContentWidth, 24),
                    Parent = contentPanel
                };

                var updateStatus = new Label {
                    Location = new Point(ContentLeft, updateVersions.Bottom + 2),
                    Size = new Point(ContentWidth, 50),
                    WrapText = true,
                    Parent = contentPanel
                };

                var checkAgain = new StandardButton {
                    Text = EventsModule.Localize("Module_update_check_again", "Check again"),
                    Width = 140,
                    Location = new Point(ContentLeft, updateStatus.Bottom + 8),
                    Parent = contentPanel
                };

                var updateNow = new StandardButton {
                    Text = EventsModule.Localize("Module_update_now", "Update now"),
                    Width = 140,
                    Location = new Point(checkAgain.Right + 10, checkAgain.Top),
                    Parent = contentPanel
                };

                Action updateModuleUpdateControls = delegate {
                    string currentVersion = string.IsNullOrWhiteSpace(module.ModuleUpdateCurrentVersion)
                        ? "—"
                        : module.ModuleUpdateCurrentVersion;
                    string latestVersion = string.IsNullOrWhiteSpace(module.ModuleUpdateLatestVersion)
                        ? "—"
                        : module.ModuleUpdateLatestVersion;
                    updateVersions.Text = string.Format(
                        EventsModule.Localize("Module_update_versions", "Current: {0}; latest checked: {1}"),
                        currentVersion,
                        latestVersion
                    );
                    updateStatus.Text = module.ModuleUpdateStatus;
                    checkAgain.Enabled = module.CanCheckForModuleUpdate;
                    updateNow.Enabled = module.CanInstallModuleUpdate;
                };

                autoUpdate.CheckedChanged += delegate(object sender, CheckChangedEvent e) {
                    module.AutoUpdateEnabled = e.Checked;
                };
                checkAgain.Click += delegate { module.RequestModuleUpdateCheck(); };
                updateNow.Click += delegate { module.RequestModuleUpdateInstall(); };

                EventHandler sourceStatusChanged = delegate { sourceStatus.Text = module.SourceStatus; };
                EventHandler moduleUpdateStatusChanged = delegate { updateModuleUpdateControls(); };
                module.SourceStatusChanged += sourceStatusChanged;
                module.ModuleUpdateStatusChanged += moduleUpdateStatusChanged;
                buildPanel.Disposed += delegate {
                    module.SourceStatusChanged -= sourceStatusChanged;
                    module.ModuleUpdateStatusChanged -= moduleUpdateStatusChanged;
                };

                updateModuleUpdateControls();
            }
        }

        private static Meta GetPreviewMeta() {
            return Meta.Events?
                       .Where(EventsModule.HasCopyableWaypoint)
                       .OrderBy(meta => meta.NextTime)
                       .FirstOrDefault();
        }

        private static string GetFormatError(EventChatMessageFormatResult result) {
            switch (result.Failure) {
                case EventChatMessageFormatFailure.EmptyFormat:
                    return EventsModule.Localize("Chat_message_format_error_empty", "The format cannot be empty.");
                case EventChatMessageFormatFailure.MissingPoint:
                    return EventsModule.Localize("Chat_message_format_error_missing_point", "The format must contain {point}.");
                case EventChatMessageFormatFailure.UnknownField:
                    return string.Format(
                        EventsModule.Localize("Chat_message_format_error_unknown_field", "Unknown field: {0}"),
                        "{" + result.FailureDetail + "}"
                    );
                case EventChatMessageFormatFailure.UnbalancedBraces:
                    return EventsModule.Localize("Chat_message_format_error_braces", "The format contains unmatched braces.");
                default:
                    return string.Empty;
            }
        }

        private void SetPosition_Click(object sender, Blish_HUD.Input.MouseEventArgs e) {
            EventsModule.ModuleInstance.ShowSetNotificationPositions();
        }

    }

}
