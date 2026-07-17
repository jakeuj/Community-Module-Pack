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
                EventHandler statusChanged = delegate { sourceStatus.Text = module.SourceStatus; };
                module.SourceStatusChanged += statusChanged;
                buildPanel.Disposed += delegate { module.SourceStatusChanged -= statusChanged; };
            }
        }

        private void SetPosition_Click(object sender, Blish_HUD.Input.MouseEventArgs e) => EventsModule.ModuleInstance.ShowSetNotificationPositions();

    }
}
