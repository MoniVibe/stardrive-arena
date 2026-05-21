using Microsoft.Xna.Framework.Graphics;
using Color = Microsoft.Xna.Framework.Color;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.Codex
{
    public class CodexCategoryListItem : ScrollListItem<CodexCategoryListItem>
    {
        public CodexEntry Entry;

        public CodexCategoryListItem(CodexEntry entry)
        {
            Entry = entry;
            // Categories (entries with children) act as expandable headers; the base
            // class requires IsHeader=true before AddSubItem will accept children.
            if (entry?.Children != null && entry.Children.Count > 0)
            {
                IsHeader = true;
                HeaderText = Localizer.Token(entry.TitleId);
            }
        }

        public override void Draw(SpriteBatch batch, DrawTimes elapsed)
        {
            base.Draw(batch, elapsed);
            if (Entry == null || IsHeader)
                return; // header text is drawn by ScrollListItem<T>.base.Draw via HeaderText

            Vector2 cursor = Pos;
            cursor.X += 15f;
            batch.DrawString(Fonts.Arial12Bold,
                Localizer.Token(Entry.TitleId), cursor, Hovered ? CodexStyles.Caption : Color.White);

            if (!string.IsNullOrEmpty(Entry.ShortDescId))
            {
                cursor.Y += Fonts.Arial12Bold.LineSpacing;
                batch.DrawString(Fonts.Arial12,
                    Localizer.Token(Entry.ShortDescId), cursor, Color.Orange);
            }
        }
    }
}
