using System.ComponentModel;

namespace NPIC_CARD_APP.Services
{
    /// <summary>Texts that are the same for every card in a batch. Edited with "Card Text..." (PropertyGrid).</summary>
    public class CardText
    {
        [Category("1. Front"), DisplayName("Issue line 1"), Description("ថ្ងៃចេញប័ណ្ណ (ចន្ទគតិ)")]
        public string IssueLine1 { get; set; }

        [Category("1. Front"), DisplayName("Issue line 2"), Description("ទីកន្លែង និងថ្ងៃចេញប័ណ្ណ (សុរិយគតិ)")]
        public string IssueLine2 { get; set; }

        [Category("1. Front"), DisplayName("Director title")]
        public string DirectorTitle { get; set; }

        [Category("1. Front"), DisplayName("Director name")]
        public string DirectorName { get; set; }

        [Category("1. Front"), DisplayName("QR template"),
         Description("Text in each student's QR code. Tokens: {StudentId} {NameEn} {NameKh}. The CSV column QrData overrides this.")]
        public string QrTemplate { get; set; }

        [Category("2. Back"), DisplayName("Address")]
        public string AddressKh { get; set; }

        [Category("2. Back"), DisplayName("Email")]
        public string Email { get; set; }

        [Category("2. Back"), DisplayName("Website")]
        public string Website { get; set; }

        [Category("2. Back"), DisplayName("Ornament (Tacteing)"),
         Description("Characters typed in the Tacteing font for the ornament under ជាតិ សាសនា ព្រះមហាក្សត្រ. Change it if the shape is not the one you want.")]
        public string OrnamentText { get; set; }

        public CardText()
        {
            IssueLine1 = "ថ្ងៃចេញប័ណ្ណ ថ្ងៃចន្ទ ១រោច ខែមិគសិរ ឆ្នាំរោង ឆស័ក ព.ស ២៥៦៨";
            IssueLine2 = "រាជធានីភ្នំពេញ ថ្ងៃទី៣០ ខែធ្នូ ឆ្នាំ២០២៥";
            DirectorTitle = "នាយកវិទ្យាស្ថាន";
            DirectorName = "តាន់ កុសល";
            QrTemplate = "{StudentId}";
            AddressKh = "ភូមិព្រៃពពេល សង្កាត់សំរោងក្រោម ខណ្ឌពោធិ៍សែនជ័យ រាជធានីភ្នំពេញ";
            Email = "info@npic.edu.kh";
            Website = "www.npic.edu.kh";
            OrnamentText = "3";
        }
    }
}
