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
        /// Prints a millimetre grid over the WHOLE page with "X,Y" labels every 10 mm.
        /// The L8050 only prints inside the ID-card area of the tray, so whatever lands on the card
        /// shows its real position: the label at the card's top-left corner = Slot X,Y.
        /// </summary>
        public void PrintPositionFinder()
        {
            Send("NPIC Card Position Finder", (g, page) =>
            {
                // Cover at least 0..330 mm in both directions, whatever page size the driver reports
                // (anything outside the real page is simply clipped).
                int maxX = (int)Math.Max(330f, page.Width + 10f);
                int maxY = (int)Math.Max(330f, page.Height + 10f);

                using (var cellA = new SolidBrush(Color.FromArgb(255, 235, 150)))   // light yellow
                using (var cellB = new SolidBrush(Color.FromArgb(170, 220, 255)))   // light blue
                using (var thin = new Pen(Color.FromArgb(90, 90, 90), 0.15f))
                using (var thick = new Pen(Color.Black, 0.35f))
                using (var font = new Font("Arial", 5f, FontStyle.Bold, GraphicsUnit.Point))
                using (var text = new SolidBrush(Color.FromArgb(210, 0, 0)))
                {
                    for (int y = 0; y < maxY; y += 10)
                        for (int x = 0; x < maxX; x += 10)
                            g.FillRectangle(((x + y) / 10) % 2 == 0 ? cellA : cellB, x, y, 10, 10);

                    for (int x = 0; x <= maxX; x += 5)
                        g.DrawLine(x % 10 == 0 ? thick : thin, x, 0, x, maxY);
                    for (int y = 0; y <= maxY; y += 5)
                        g.DrawLine(y % 10 == 0 ? thick : thin, 0, y, maxX, y);

                    for (int y = 0; y < maxY; y += 10)
                        for (int x = 0; x < maxX; x += 10)
                            g.DrawString(x + "," + y, font, text, x + 0.5f, y + 0.5f);
                }
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
                    PrinterDevMode.Apply(doc.DefaultPageSettings, devMode);   // paper source / size / orientation for this job
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
            return IsCardSource(CreatePrinterSettings().DefaultPageSettings.PaperSource.SourceName);
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

            string source = ps.DefaultPageSettings.PaperSource.SourceName ?? "";
            if (!IsCardSource(source))
                problems.Add("Paper Source is \"" + source + "\", not the ID card tray. Fix it in 'Driver Setup...'.");

            SizeF page = PageSizeMm(ps.DefaultPageSettings);
            CheckSlot(problems, 1, _settings.Slot1X, _settings.Slot1Y, page);
            if (_settings.UseTwoSlots) CheckSlot(problems, 2, _settings.Slot2X, _settings.Slot2Y, page);

            problems.AddRange(PrinterStatus.Check(_settings.PrinterName));
            return problems;
        }

        /// <summary>A card drawn outside the page is cut off by Windows → the card comes out blank.</summary>
        private static void CheckSlot(List<string> problems, int slot, float x, float y, SizeF page)
        {
            const float tolerance = 3f;
            if (x < -tolerance || y < -tolerance ||
                x + CardRenderer.CardWidthMm > page.Width + tolerance ||
                y + CardRenderer.CardHeightMm > page.Height + tolerance)
            {
                problems.Add(string.Format(CultureInfo.InvariantCulture,
                    "Slot {0} (X {1:0.0}, Y {2:0.0} mm, card {3:0.0} x {4:0.0} mm) is outside the page " +
                    "({5:0.0} x {6:0.0} mm), so it will print blank. Use 'Find Position'.",
                    slot, x, y, CardRenderer.CardWidthMm, CardRenderer.CardHeightMm, page.Width, page.Height));
            }
        }

        /// <summary>Page size in mm, already swapped for landscape.</summary>
        private static SizeF PageSizeMm(PageSettings page)
        {
            Rectangle b = page.Bounds;   // hundredths of an inch, orientation applied
            return new SizeF(b.Width * HundredthInchToMm, b.Height * HundredthInchToMm);
        }

        public string DescribePage()
        {
            PageSettings page = CreatePrinterSettings().DefaultPageSettings;
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
