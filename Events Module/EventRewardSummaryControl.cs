using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Events_Module {

    internal sealed class EventRewardSummaryControl : Control {

        private const int IconSize = 16;

        private readonly EventRewardSummary _reward;
        private readonly AsyncTexture2D _rareIcon;
        private readonly AsyncTexture2D _dragoniteIcon;

        public EventRewardSummaryControl(EventRewardSummary reward,
                                         AsyncTexture2D rareIcon,
                                         AsyncTexture2D dragoniteIcon) {
            _reward = reward;
            _rareIcon = rareIcon;
            _dragoniteIcon = dragoniteIcon;
            Size = new Point(CalculateWidth(reward), 33);
        }

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds) {
            int x = 0;

            if (_reward.MinimumRareOrExoticItems.HasValue) {
                DrawRewardIcon(spriteBatch, _rareIcon, "R", x);
                spriteBatch.DrawStringOnCtrl(
                    this,
                    "≥" + _reward.MinimumRareOrExoticItems.Value,
                    Content.DefaultFont14,
                    new Rectangle(x + 16, 0, 20, Height),
                    Color.White,
                    false,
                    true,
                    0,
                    HorizontalAlignment.Left,
                    VerticalAlignment.Middle
                );
                x += 36;
            }

            if (!string.IsNullOrWhiteSpace(_reward.CompactDragoniteAmount)) {
                DrawRewardIcon(spriteBatch, _dragoniteIcon, "D", x);
                spriteBatch.DrawStringOnCtrl(
                    this,
                    _reward.CompactDragoniteAmount,
                    Content.DefaultFont14,
                    new Rectangle(x + 16, 0, 40, Height),
                    Color.White,
                    false,
                    true,
                    0,
                    HorizontalAlignment.Left,
                    VerticalAlignment.Middle
                );
                x += 56;
            }

            if (_reward.GuaranteedCoinCopper.HasValue) {
                if (x > 0) x += 4;
                spriteBatch.DrawStringOnCtrl(
                    this,
                    EventRewardTextFormatter.FormatCoin(_reward.GuaranteedCoinCopper.Value),
                    Content.DefaultFont14,
                    new Rectangle(x, 0, 40, Height),
                    ContentService.Colors.Chardonnay,
                    false,
                    true,
                    0,
                    HorizontalAlignment.Left,
                    VerticalAlignment.Middle
                );
            }
        }

        private static int CalculateWidth(EventRewardSummary reward) {
            int width = 0;
            if (reward?.MinimumRareOrExoticItems.HasValue == true) width += 36;
            if (!string.IsNullOrWhiteSpace(reward?.CompactDragoniteAmount)) width += 56;
            if (reward?.GuaranteedCoinCopper.HasValue == true) width += (width > 0 ? 4 : 0) + 40;
            return width;
        }

        private void DrawRewardIcon(SpriteBatch spriteBatch, AsyncTexture2D texture, string fallback, int x) {
            if (texture != null && texture.HasTexture) {
                spriteBatch.DrawOnCtrl(this, texture, new Rectangle(x, (Height - IconSize) / 2, IconSize, IconSize));
                return;
            }

            spriteBatch.DrawStringOnCtrl(
                this,
                fallback,
                Content.DefaultFont14,
                new Rectangle(x, 0, IconSize, Height),
                ContentService.Colors.Chardonnay,
                false,
                true,
                0,
                HorizontalAlignment.Center,
                VerticalAlignment.Middle
            );
        }
    }
}
