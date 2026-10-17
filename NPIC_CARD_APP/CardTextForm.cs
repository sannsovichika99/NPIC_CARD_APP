using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.IO;
using System.Windows.Forms;
using System.Reflection;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// "Card Text…": edits the texts that are the same on every card (issue lines, director, address, QR template…).
    /// Works on a copy: Save returns it in <see cref="Result"/>, Cancel / Esc / ✕ leave the settings untouched.
    /// </summary>
    public class CardTextForm : ModalForm
    {
        private readonly PropertyGrid _grid;
        private CardText _edit;

        /// <summary>The edited texts after Save (DialogResult.OK).</summary>
        public CardText Result { get { return _edit; } }

        public CardTextForm(CardText current)
        {
            _edit = Copy(current ?? new CardText());

            Text = "Card Text";
            Subtitle = "Fixed text printed on every card — front and back";
            Glyph = Theme.IcoFont;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(760, work.Width), Math.Min(560, work.Height));
            MinimumSize = new Size(Math.Min(560, work.Width), Math.Min(420, work.Height));
            Font = new Font(CardFonts.UI, 9f);

            _grid = new PropertyGrid
            {
                Dock = DockStyle.Fill,
                SelectedObject = _edit,
                PropertySort = PropertySort.Categorized,
                ToolbarVisible = false,
                HelpVisible = true,
                Font = new Font(CardFonts.UI, 9.75f),
                BackColor = Theme.Surface,
                ViewBackColor = Theme.Surface,
                ViewForeColor = Theme.TextMain,
                ViewBorderColor = Theme.Border,
                LineColor = Theme.AppBack,
                CategoryForeColor = Theme.Accent,
                CategorySplitterColor = Theme.Border,
                HelpBackColor = Theme.SurfaceAlt,
                HelpForeColor = Theme.TextSoft,
                HelpBorderColor = Theme.Border,
                SelectedItemWithFocusBackColor = Theme.ButtonA,
                SelectedItemWithFocusForeColor = Color.White,
                DisabledItemForeColor = Theme.TextMuted
            };
            var gridHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 14, 16, 14), BackColor = Theme.Surface };
            gridHost.Controls.Add(_grid);

            // ---- Footer: Reset on the left, Cancel / Save on the right ----
            var footer = ModalUi.Footer();
            Button save = ModalUi.Button("Save", PillKind.Primary);
            save.DialogResult = DialogResult.OK;
            Button cancel = ModalUi.Button("Cancel", PillKind.Secondary);
            cancel.DialogResult = DialogResult.Cancel;
            Button reset = ModalUi.Button("Reset to Defaults", PillKind.Ghost, (s, e) => ResetDefaults());
            reset.Margin = new Padding(0, 0, 24, 0);
            footer.Controls.Add(save);
            footer.Controls.Add(cancel);
            footer.Controls.Add(reset);
            new ToolTip().SetToolTip(reset, "Put back the original NPIC texts (nothing is saved until you click Save)");

            Controls.Add(gridHost);
            Controls.Add(footer);
            AcceptButton = save;   // Enter while typing a value commits it; Enter on the grid saves
            CancelButton = cancel;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            NarrowNameColumn(170);
            _grid.Focus();
        }

        /// <summary>The grid splits 50/50 by default; the Khmer values need the room. No public API, so via reflection.</summary>
        private void NarrowNameColumn(int width)
        {
            try
            {
                foreach (Control c in _grid.Controls)
                {
                    if (c.GetType().Name != "PropertyGridView") continue;
                    MethodInfo move = c.GetType().GetMethod("MoveSplitterTo", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (move != null) move.Invoke(c, new object[] { width });
                }
            }
            catch (Exception)
            {
                // purely cosmetic: keep the default split
            }
        }

        private void ResetDefaults()
        {
            if (AppDialog.Show(this, "Replace all card texts with the original NPIC texts?\n\nNothing is saved until you click Save.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            _edit = new CardText();
            _grid.SelectedObject = _edit;
        }

        /// <summary>Copy of every read/write property, so new CardText fields are picked up automatically.</summary>
        private static CardText Copy(CardText source)
        {
            var copy = new CardText();
            foreach (PropertyInfo p in typeof(CardText).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                    p.SetValue(copy, p.GetValue(source, null), null);
            return copy;
        }
    }

    /// <summary>
    /// "…" button for CardText.BackQrLogoPath: picks an image and copies it to
    /// %AppData%\NPIC_CARD_APP\Logos, so the card still prints if the original file is moved or deleted.
    /// </summary>
    public class QrLogoFileEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
        {
            return UITypeEditorEditStyle.Modal;
        }

        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            string current = CardText.ResolveAppPath(value as string);   // "Assets\npic_png.png" -> full path
            using (var dlg = new OpenFileDialog
            {
                Title = "Logo for the centre of the back QR code",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
            })
            {
                dlg.InitialDirectory = !string.IsNullOrEmpty(current) && File.Exists(current)
                    ? Path.GetDirectoryName(current) : AssetStore.Folder;
                if (dlg.ShowDialog() != DialogResult.OK) return value;

                try
                {
                    // Make sure it really is an image before keeping it
                    using (var fs = new FileStream(dlg.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (Image.FromStream(fs)) { }

                    // A picture shipped in Assets travels with the app: keep it relative (like the default) instead of copying it.
                    string assets = Path.GetFullPath(AssetStore.Folder).TrimEnd('\\') + "\\";
                    string picked = Path.GetFullPath(dlg.FileName);
                    if (picked.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                        return "Assets\\" + picked.Substring(assets.Length);

                    string folder = Path.Combine(Path.GetDirectoryName(AppSettings.FilePath), "Logos");
                    Directory.CreateDirectory(folder);
                    // Unique name: Cancel in Card Text must not change the logo already in use
                    string target = Path.Combine(folder, "qr_logo_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                                                         Path.GetExtension(dlg.FileName).ToLowerInvariant());
                    File.Copy(dlg.FileName, target, true);
                    return target;
                }
                catch (Exception ex)
                {
                    AppDialog.Show("Could not use this file as a logo:\n\n" + ex.Message, "QR logo",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return value;
                }
            }
        }
    }
}
