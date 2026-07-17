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
            Size = new Point(92, 33);
        }

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds) {
            DrawRewardIcon(spriteBatch, _rareIcon, "R", 0);
            spriteBatch.DrawStringOnCtrl(
                this,
                "≥" + _reward.MinimumRareOrExoticItems,
                Content.DefaultFont14,
                new Rectangle(16, 0, 20, Height),
                Color.White,
                false,
                true,
                0,
                HorizontalAlignment.Left,
                VerticalAlignment.Middle
            );

            DrawRewardIcon(spriteBatch, _dragoniteIcon, "D", 36);
            spriteBatch.DrawStringOnCtrl(
                this,
                _reward.CompactDragoniteAmount,
                Content.DefaultFont14,
                new Rectangle(52, 0, 40, Height),
                Color.White,
                false,
                true,
                0,
                HorizontalAlignment.Left,
                VerticalAlignment.Middle
            );
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
