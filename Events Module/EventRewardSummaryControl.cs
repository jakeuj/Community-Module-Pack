using System.Globalization;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Events_Module {

    internal sealed class EventRewardSummaryControl : Control {

        private const int IconSize = 16;
        private const int CompactRewardWidth = 36;
        private const int DragoniteRewardWidth = 56;
        private const int CoinTextFallbackWidth = 40;

        private readonly EventRewardSummary _reward;
        private readonly AsyncTexture2D _rareIcon;
        private readonly AsyncTexture2D _dragoniteIcon;
        private readonly AsyncTexture2D _coinIcon;

        public EventRewardSummaryControl(EventRewardSummary reward,
                                         AsyncTexture2D rareIcon,
                                         AsyncTexture2D dragoniteIcon,
                                         AsyncTexture2D coinIcon) {
            _reward = reward;
            _rareIcon = rareIcon;
            _dragoniteIcon = dragoniteIcon;
            _coinIcon = coinIcon;
            Size = new Point(CalculateWidth(reward), 33);
        }

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds) {
            int x = 0;

            if (_reward.GuaranteedCoinCopper.HasValue) {
                int coinCopper = _reward.GuaranteedCoinCopper.Value;
                bool isWholeGold = IsWholeGold(coinCopper);
                int coinWidth = CalculateCoinWidth(coinCopper);
                int textOffset = 0;
                int textWidth = coinWidth;
                string coinText = EventRewardTextFormatter.FormatCoin(coinCopper);

                if (isWholeGold) {
                    DrawRewardIcon(spriteBatch, _coinIcon, "G", x);
                    textOffset = IconSize;
                    textWidth -= IconSize;
                    coinText = (coinCopper / 10000).ToString(CultureInfo.InvariantCulture);
                }

                spriteBatch.DrawStringOnCtrl(
                    this,
                    coinText,
                    Content.DefaultFont14,
                    new Rectangle(x + textOffset, 0, textWidth, Height),
                    ContentService.Colors.Chardonnay,
                    false,
                    true,
                    0,
                    HorizontalAlignment.Left,
                    VerticalAlignment.Middle
                );

                x += coinWidth;
            }

            if (_reward.MinimumRareOrExoticItems.HasValue) {
                DrawRewardIcon(spriteBatch, _rareIcon, "R", x);
                spriteBatch.DrawStringOnCtrl(
                    this,
                    "≥" + _reward.MinimumRareOrExoticItems.Value,
                    Content.DefaultFont14,
                    new Rectangle(x + IconSize, 0, CompactRewardWidth - IconSize, Height),
                    Color.White,
                    false,
                    true,
                    0,
                    HorizontalAlignment.Left,
                    VerticalAlignment.Middle
                );
                x += CompactRewardWidth;
            }

            if (!string.IsNullOrWhiteSpace(_reward.CompactDragoniteAmount)) {
                DrawRewardIcon(spriteBatch, _dragoniteIcon, "D", x);
                spriteBatch.DrawStringOnCtrl(
                    this,
                    _reward.CompactDragoniteAmount,
                    Content.DefaultFont14,
                    new Rectangle(x + IconSize, 0, DragoniteRewardWidth - IconSize, Height),
                    Color.White,
                    false,
                    true,
                    0,
                    HorizontalAlignment.Left,
                    VerticalAlignment.Middle
                );
                x += DragoniteRewardWidth;
            }
        }

        private static int CalculateWidth(EventRewardSummary reward) {
            int width = 0;

            if (reward?.GuaranteedCoinCopper.HasValue == true) {
                width += CalculateCoinWidth(reward.GuaranteedCoinCopper.Value);
            }

            if (reward?.MinimumRareOrExoticItems.HasValue == true) width += CompactRewardWidth;
            if (!string.IsNullOrWhiteSpace(reward?.CompactDragoniteAmount)) width += DragoniteRewardWidth;
            return width;
        }

        private static int CalculateCoinWidth(int coinCopper) {
            return IsWholeGold(coinCopper) ? CompactRewardWidth : CoinTextFallbackWidth;
        }

        private static bool IsWholeGold(int coinCopper) {
            return coinCopper > 0 && coinCopper % 10000 == 0;
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
