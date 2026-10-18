using System;

namespace NPIC_CARD_APP.Rendering
{
    /// <summary>Card design chosen with Card Template…. Every template prints the same data on a CR80 card.</summary>
    public enum CardTemplate
    {
        /// <summary>The original NPIC design (card_front.png / card_back.png).</summary>
        Classic,
        /// <summary>Navy header band with the title, photo on the left, footer bar.</summary>
        Modern,
        /// <summary>Clean white card: large name, photo on the right, hairline divider.</summary>
        Minimal,
        /// <summary>Navy ribbon down the left edge holding the logo, QR code and generation.</summary>
        Ribbon
    }

    /// <summary>Name and short description of a <see cref="CardTemplate"/> for the picker.</summary>
    public sealed class CardTemplateInfo
    {
        public CardTemplate Template { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }

        public CardTemplateInfo(CardTemplate template, string name, string description)
        {
            Template = template;
            Name = name;
            Description = description;
        }
    }

    public static class CardTemplates
    {
        public const CardTemplate Default = CardTemplate.Classic;

        public static readonly CardTemplateInfo[] All =
        {
            new CardTemplateInfo(CardTemplate.Classic, "Classic",
                "The original NPIC card: faded shield, photo and QR on the left, NPIC watermark on the back."),
            new CardTemplateInfo(CardTemplate.Modern, "Modern",
                "Navy header band with the logo and title, framed photo, navy footer bar. Back with the logos in the band."),
            new CardTemplateInfo(CardTemplate.Minimal, "Minimal",
                "Clean white card with the name in large type, photo on the right and a hairline divider. Uses the least ink."),
            new CardTemplateInfo(CardTemplate.Ribbon, "Ribbon",
                "Navy ribbon down the left edge with the logo, QR code and generation number. Logos and social icons in the ribbon on the back.")
        };

        public static CardTemplateInfo Find(CardTemplate template)
        {
            foreach (CardTemplateInfo info in All)
                if (info.Template == template) return info;
            return All[0];
        }

        /// <summary>Saved text → template; empty or unknown (e.g. from a newer version) = <see cref="Default"/>.</summary>
        public static CardTemplate Parse(string text)
        {
            CardTemplate t;
            if (!string.IsNullOrEmpty(text) && Enum.TryParse(text, true, out t) && Enum.IsDefined(typeof(CardTemplate), t)) return t;
            return Default;
        }
    }
}
