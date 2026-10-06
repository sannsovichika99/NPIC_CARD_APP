using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Globalization;
using System.Text;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP.Printing
{
    public class CardPrinter
    {
        private const float HundredthInchToMm = 0.254f;
        private readonly AppSettings _settings;

        /// <summary>Paper / source / size Windows actually used for the last job (filled while printing).</summary>
        public string LastJobInfo { get; private set; }

        public CardPrinter(AppSettings settings)
        {
            _settings = settings;
        }

        public PrinterSettings CreatePrinterSettings()
        {
            var ps = new PrinterSettings { PrinterName = _settings.PrinterName };
            if (!ps.IsValid)
                throw new InvalidOperationException("Printer not found: " + _settings.PrinterName);

            byte[] devMode = SavedDevMode();
            if (devMode != null)
            {
                try
                {
                    PrinterDevMode.Apply(ps, devMode);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Saved driver settings could not be applied. Open 'Driver Setup...' again.", ex);
                }
            }
            return ps;
        }

        /// <summary>The DEVMODE saved by 'Driver Setup...', or null. Throws if it belongs to another printer.</summary>
        private byte[] SavedDevMode()
        {
            if (string.IsNullOrEmpty(_settings.DevModeBase64)) return null;

            byte[] devMode;
            try { devMode = Convert.FromBase64String(_settings.DevModeBase64); }
            catch (FormatException) { throw new InvalidOperationException("Saved driver settings are damaged. Open 'Driver Setup...' again."); }

            // DEVMODEW starts with dmDeviceName: 32 UTF-16 chars (the printer name, cut to 31 chars)
            if (devMode.Length >= 64)
            {
                string device = Encoding.Unicode.GetString(devMode, 0, 64);
                int end = device.IndexOf('\0');
                if (end >= 0) device = device.Substring(0, end);
                if (device.Length > 0 && !_settings.PrinterName.StartsWith(device, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Saved driver settings are for \"" + device + "\", not \"" + _settings.PrinterName +
                        "\".\nOpen 'Driver Setup...' again.");
                }
            }
            return devMode;
        }

        /// <summary>Prints one tray load. slot1 / slot2 may be null.</summary>
        public void Print(Bitmap slot1, Bitmap slot2, string documentName)
        {
            Send(documentName, (g, page) => DrawLoad(g, slot1, slot2));
        }

        /// <summary>Draws one tray load (slot1 / slot2 may be null) at the calibrated slot positions.</summary>
        public void DrawLoad(Graphics g, Bitmap slot1, Bitmap slot2)
        {
            if (slot1 != null) DrawCard(g, slot1, _settings.Slot1X, _settings.Slot1Y);
            if (slot2 != null) DrawCard(g, slot2, _settings.Slot2X, _settings.Slot2Y);
        }

        /// <summary>
        /// Prints the <see cref="PositionGrid"/> over the WHOLE page. The L8050 only prints inside the ID-card
        /// area of the tray, so whatever lands on a card shows where that slot really is (see FindPositionForm).
        /// </summary>
        public void PrintPositionFinder()
        {
            Send("NPIC Card Position Finder", (g, page) =>
            {
                // Cover at least 0..330 mm in both directions, whatever page size the driver reports
                // (anything outside the real page is simply clipped).
                float maxX = Math.Max(330f, page.Width + 10f);
                float maxY = Math.Max(330f, page.Height + 10f);
                PositionGrid.Draw(g, new RectangleF(0f, 0f, maxX, maxY));
            });
        }

        /// <summary>
        /// Builds a print job with the saved driver settings, millimetre units and the physical page corner as origin.
        /// drawPage(g, pageSizeMm, pageIndex) is called once per page. Used for printing AND for Print Preview.
        /// The caller disposes the document.
        /// </summary>
        public PrintDocument CreateDocument(string documentName, int pageCount, Action<Graphics, SizeF, int> drawPage)
        {
            var doc = new PrintDocument();
            try
            {
                doc.PrinterSettings = CreatePrinterSettings();
                byte[] devMode = SavedDevMode();
                if (devMode != null)
                    PrinterDevMode.Apply(doc.DefaultPageSettings, devMode);   // paper source / size for this job
                // Slot X/Y, the tray view and the card pockets all assume a portrait page. A "Landscape" left in
                // the saved driver settings would make Windows turn the page 90° → the card prints sideways.
                doc.DefaultPageSettings.Landscape = false;
                doc.DocumentName = documentName;
                doc.OriginAtMargins = false;
                doc.PrintController = new StandardPrintController(); // no "Printing page 1" popup

                int pageIndex = 0;
                doc.BeginPrint += (sender, e) => pageIndex = 0;     // preview may render the document more than once
                doc.PrintPage += (sender, e) =>
                {
                    bool preview = doc.PrintController != null && doc.PrintController.IsPreview;

                    PageSettings used = e.PageSettings;
                    SizeF page = new SizeF(e.PageBounds.Width * HundredthInchToMm, e.PageBounds.Height * HundredthInchToMm);
                    LastJobInfo = string.Format(CultureInfo.InvariantCulture,
                        "Paper: {0} ({1:0.0} x {2:0.0} mm{3})\nSource: {4}",
                        used.PaperSize.PaperName, page.Width, page.Height,
                        used.Landscape ? ", landscape" : "", used.PaperSource.SourceName);

                    Graphics g = e.Graphics;
                    g.PageUnit = GraphicsUnit.Millimeter;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    // On paper the origin is the printable-area corner → move it to the physical page corner.
                    // The preview already starts at the page corner, so it must not be shifted.
                    if (!preview)
                        g.TranslateTransform(-e.PageSettings.HardMarginX * HundredthInchToMm,
                                             -e.PageSettings.HardMarginY * HundredthInchToMm);

                    drawPage(g, page, pageIndex);
                    if (preview) DrawSlotOutlines(g);

                    pageIndex++;
                    e.HasMorePages = pageIndex < pageCount;
                };
                return doc;
            }
            catch
            {
                doc.Dispose();
                throw;
            }
        }

        /// <summary>Preview only: dashed red frames where the tray slots are (never printed).</summary>
        private void DrawSlotOutlines(Graphics g)
        {
            using (var pen = new Pen(Color.Red, 0.3f) { DashStyle = DashStyle.Dash })
            using (var font = new Font("Arial", 6f, FontStyle.Bold, GraphicsUnit.Point))
            using (var brush = new SolidBrush(Color.Red))
            {
                g.DrawRectangle(pen, _settings.Slot1X, _settings.Slot1Y, CardRenderer.CardWidthMm, CardRenderer.CardHeightMm);
                g.DrawString("Slot 1", font, brush, _settings.Slot1X, _settings.Slot1Y - 3f);
                if (_settings.UseTwoSlots)
                {
                    g.DrawRectangle(pen, _settings.Slot2X, _settings.Slot2Y, CardRenderer.CardWidthMm, CardRenderer.CardHeightMm);
                    g.DrawString("Slot 2", font, brush, _settings.Slot2X, _settings.Slot2Y - 3f);
                }
            }
        }

        /// <summary>Prints a single-page job.</summary>
        private void Send(string documentName, Action<Graphics, SizeF> draw)
        {
            using (PrintDocument doc = CreateDocument(documentName, 1, (g, page, index) => draw(g, page)))
            {
                try
                {
                    doc.Print();
                }
                catch (InvalidPrinterException ex)
                {
                    throw new InvalidOperationException(
                        "Windows says the printer settings are not valid for \"" + _settings.PrinterName +
                        "\". Check that the printer is installed, then open 'Driver Setup...' again.", ex);
                }
                catch (Win32Exception ex)
                {
                    throw new InvalidOperationException(string.Format(
                        "Windows could not send the job to \"{0}\" (error {1}: {2}).\n" +
                        "Check that the printer is on and connected, then click 'Check Printer'.",
                        _settings.PrinterName, ex.NativeErrorCode, ex.Message), ex);
                }
            }
        }

        private static void DrawCard(Graphics g, Bitmap image, float xMm, float yMm)
        {
            g.DrawImage(image, new RectangleF(xMm, yMm, CardRenderer.CardWidthMm, CardRenderer.CardHeightMm));
        }

        public bool IsCardTraySelected()
        {
            return IsCardSource(PortraitPage(CreatePrinterSettings()).PaperSource.SourceName);
        }

        private static bool IsCardSource(string source)
        {
            source = source ?? "";
            return source.IndexOf("Card", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("PVC", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("Disc", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("CD", StringComparison.Ordinal) >= 0;
        }

        /// <summary>Everything that would stop a card from printing. Empty list = ready.</summary>
        public List<string> FindProblems()
        {
            var problems = new List<string>();
            PrinterSettings ps;
            try
            {
                ps = CreatePrinterSettings();
            }
            catch (Exception ex)
            {
                problems.Add(ex.Message);
                return problems;
            }

            if (string.IsNullOrEmpty(_settings.DevModeBase64))
                problems.Add("'Driver Setup...' has not been done, so Paper Source / Paper Type are still the driver defaults.");

            string source = PortraitPage(ps).PaperSource.SourceName ?? "";
            if (!IsCardSource(source))
                problems.Add("Paper Source is \"" + source + "\", not the ID card tray. Fix it in 'Driver Setup...'.");

            SizeF page = PageSizeMm(PortraitPage(ps));
            AddIfNotNull(problems, SlotProblem(1, _settings.Slot1X, _settings.Slot1Y, page));
            // In Auto mode an unusable slot 2 is not a problem: CardsPerLoad() simply prints 1 card per load.
            if (_settings.SlotMode == SlotMode.Two) AddIfNotNull(problems, Slot2Problem(page));

            problems.AddRange(PrinterStatus.Check(_settings.PrinterName));
            return problems;
        }

        /// <summary>
        /// How many cards go in one tray load when printing cardCount cards.
        /// Auto: 2 when there are 2+ cards and slot 2 is usable (on the page, not overlapping slot 1), else 1.
        /// reason = why Auto fell back to 1 slot although there are 2+ cards, otherwise null. Never throws.
        /// </summary>
        public int CardsPerLoad(int cardCount, out string reason)
        {
            reason = null;
            switch (_settings.SlotMode)
            {
                case SlotMode.One: return 1;
                case SlotMode.Two: return 2;
            }
            if (cardCount < 2) return 1;

            SizeF? page = null;
            try { page = PageSizeMm(PortraitPage(CreatePrinterSettings())); }
            catch { /* printer not available: only the overlap check is possible */ }

            reason = Slot2Problem(page);
            return reason == null ? 2 : 1;
        }

        /// <summary>Why slot 2 cannot be used (outside the page or on top of slot 1), or null when it is fine.</summary>
        private string Slot2Problem(SizeF? page)
        {
            float dx = Math.Abs(_settings.Slot2X - _settings.Slot1X);
            float dy = Math.Abs(_settings.Slot2Y - _settings.Slot1Y);
            if (dx < CardRenderer.CardWidthMm - 1f && dy < CardRenderer.CardHeightMm - 1f)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "Slot 2 (X {0:0.0}, Y {1:0.0} mm) overlaps slot 1 (X {2:0.0}, Y {3:0.0} mm). " +
                    "Use 'Find Position' to measure slot 2.",
                    _settings.Slot2X, _settings.Slot2Y, _settings.Slot1X, _settings.Slot1Y);
            }
            return page.HasValue ? SlotProblem(2, _settings.Slot2X, _settings.Slot2Y, page.Value) : null;
        }

        /// <summary>A card drawn outside the page is cut off by Windows → the card comes out blank.</summary>
        private static string SlotProblem(int slot, float x, float y, SizeF page)
        {
            const float tolerance = 3f;
            if (x < -tolerance || y < -tolerance ||
                x + CardRenderer.CardWidthMm > page.Width + tolerance ||
                y + CardRenderer.CardHeightMm > page.Height + tolerance)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "Slot {0} (X {1:0.0}, Y {2:0.0} mm, card {3:0.0} x {4:0.0} mm) is outside the page " +
                    "({5:0.0} x {6:0.0} mm), so it will print blank. Use 'Find Position'.",
                    slot, x, y, CardRenderer.CardWidthMm, CardRenderer.CardHeightMm, page.Width, page.Height);
            }
            return null;
        }

        private static void AddIfNotNull(List<string> list, string item)
        {
            if (item != null) list.Add(item);
        }

        /// <summary>
        /// The page as it is printed: always portrait (see CreateDocument). DefaultPageSettings is a new object
        /// on every call, so the orientation has to be set on the copy that is actually read.
        /// </summary>
        private static PageSettings PortraitPage(PrinterSettings ps)
        {
            PageSettings page = ps.DefaultPageSettings;
            page.Landscape = false;
            return page;
        }

        /// <summary>Page size in mm (portrait, as printed).</summary>
        private static SizeF PageSizeMm(PageSettings page)
        {
            Rectangle b = page.Bounds;   // hundredths of an inch, orientation applied
            return new SizeF(b.Width * HundredthInchToMm, b.Height * HundredthInchToMm);
        }

        /// <summary>Page size (mm) of the selected printer, or null when it cannot be read.</summary>
        public SizeF? TryGetPageSizeMm()
        {
            try { return PageSizeMm(PortraitPage(CreatePrinterSettings())); }
            catch { return null; }
        }

        public string DescribePage()
        {
            PageSettings page = PortraitPage(CreatePrinterSettings());
            return string.Format(CultureInfo.InvariantCulture,
                "Paper: {0} ({1:0.0} x {2:0.0} mm{6})  |  Source: {3}  |  Hard margin: {4:0.0} / {5:0.0} mm",
                page.PaperSize.PaperName,
                PageSizeMm(page).Width,
                PageSizeMm(page).Height,
                page.PaperSource.SourceName,
                page.HardMarginX * HundredthInchToMm,
                page.HardMarginY * HundredthInchToMm,
                page.Landscape ? ", landscape" : "");
        }
    }
}
