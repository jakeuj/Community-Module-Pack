using System;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Microsoft.Xna.Framework;

namespace Events_Module {
    public class BasicSettingsView : View {

        protected override void Build(Container buildPanel) {
            var setPosition = new StandardButton {
                Text     = EventsModule.Localize("Set_notification_position", "Set Notification Position"),
                Width    = 196,
                Location = new Point(32, 32),
                Parent   = buildPanel
            };

            setPosition.Click += SetPosition_Click;

            var sourceTitle = new Label {
                Text = EventsModule.Localize("Official_timer_data", "Event timer data"),
                Location = new Point(32, 86),
                Size = new Point(640, 24),
                Font = Blish_HUD.ContentService.Content.DefaultFont16,
                Parent = buildPanel
            };

            var sourceStatus = new Label {
                Text = EventsModule.ModuleInstance?.SourceStatus ?? string.Empty,
                Location = new Point(32, sourceTitle.Bottom + 6),
                Size = new Point(640, 66),
                WrapText = true,
                Parent = buildPanel
            };

            var refresh = new StandardButton {
                Text = EventsModule.Localize("Refresh_official_event_timer", "Refresh official event timer"),
                Width = 220,
                Location = new Point(32, sourceStatus.Bottom + 8),
                Parent = buildPanel
            };

            refresh.Click += delegate { EventsModule.ModuleInstance?.RequestOfficialRefresh(); };

            EventsModule module = EventsModule.ModuleInstance;
            if (module != null) {
                var updateTitle = new Label {
                    Text = EventsModule.Localize("Module_update_title", "Module updates"),
                    Location = new Point(32, refresh.Bottom + 30),
                    Size = new Point(640, 24),
                    Font = Blish_HUD.ContentService.Content.DefaultFont16,
                    Parent = buildPanel
                };

                var autoUpdate = new Checkbox {
                    Text = EventsModule.Localize("Module_auto_update", "Automatically update this module"),
                    BasicTooltipText = EventsModule.Localize(
                        "Module_auto_update_tooltip",
                        "Install stable GitHub releases at the next module load. A successful update restarts Blish HUD immediately."
                    ),
                    Checked = module.AutoUpdateEnabled,
                    Location = new Point(32, updateTitle.Bottom + 8),
                    Parent = buildPanel
                };

                var updateVersions = new Label {
                    Location = new Point(32, autoUpdate.Bottom + 8),
                    Size = new Point(640, 24),
                    Parent = buildPanel
                };

                var updateStatus = new Label {
                    Location = new Point(32, updateVersions.Bottom + 2),
                    Size = new Point(640, 50),
                    WrapText = true,
                    Parent = buildPanel
                };

                var checkAgain = new StandardButton {
                    Text = EventsModule.Localize("Module_update_check_again", "Check again"),
                    Width = 140,
                    Location = new Point(32, updateStatus.Bottom + 8),
                    Parent = buildPanel
                };

                var updateNow = new StandardButton {
                    Text = EventsModule.Localize("Module_update_now", "Update now"),
                    Width = 140,
                    Location = new Point(checkAgain.Right + 10, checkAgain.Top),
                    Parent = buildPanel
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

                autoUpdate.CheckedChanged += delegate (object sender, CheckChangedEvent e) {
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

        private void SetPosition_Click(object sender, Blish_HUD.Input.MouseEventArgs e) => EventsModule.ModuleInstance.ShowSetNotificationPositions();

    }
}
