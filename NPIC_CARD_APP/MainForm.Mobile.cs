using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Main window ↔ Card Studio mobile app (Mobile App…, Ctrl+Shift+M). The server runs for the whole session once started,
    /// also while the Mobile App page is closed. Its requests arrive on server threads: everything that touches the student
    /// list, the print history map or the card renderer runs on the UI thread through <see cref="OnUi{T}"/>.
    /// </summary>
    public partial class MainForm : IMobileLinkHost
    {
        private MobileLinkServer _mobile;
        private bool _mobilePrintRunning;   // a print asked from the phone is waiting or running (UI thread only)

        private MobileLinkServer Mobile
        {
            get { return _mobile ?? (_mobile = new MobileLinkServer(this)); }
        }

        private void ShowMobileLink()
        {
            using (var form = new MobileLinkForm(Mobile, _settings, () => SaveSettings(false)))
                form.ShowDialog(this);
        }

        /// <summary>Starts the link at start-up when it was left on; a problem only shows in the status line and the bell.</summary>
        private void StartMobileLinkQuietly()
        {
            if (!_settings.MobileLinkEnabled) return;
            string token = _settings.MobileLinkToken;
            if (string.IsNullOrEmpty(token)) return;   // settings from another PC / Windows user: pair again on the Mobile App page
            int port = _settings.MobileLinkPort <= 0 ? MobileLinkServer.DefaultPort : _settings.MobileLinkPort;
            try
            {
                Mobile.Start(port, token);
            }
            catch (SocketException ex)
            {
                SetStatus("Could not start the mobile app link on port " + port + ": " + ex.Message);
            }
        }

        private void StopMobileLink()
        {
            if (_mobile != null) _mobile.Dispose();
        }

        // ===================== IMobileLinkHost (server threads) =====================
        List<Student> IMobileLinkHost.Students()
        {
            return OnUi(() => _students.ToList());
        }

        string IMobileLinkHost.Source()
        {
            return OnUi(() => _students.Count == 0 ? "" : _source ?? "");
        }

        AppSettings IMobileLinkHost.Settings()
        {
            return _settings;   // same object for the whole session; the server only reads folder paths and the design
        }

        Dictionary<string, DateTime> IMobileLinkHost.LastPrinted()
        {
            return OnUi(() => new Dictionary<string, DateTime>(_lastPrinted, StringComparer.OrdinalIgnoreCase));
        }

        byte[] IMobileLinkHost.RenderCard(Student s, bool front, int maxWidth)
        {
            // The renderer shares cached fonts and pictures, so it is only ever used from the UI thread.
            return OnUi(() =>
            {
                using (Bitmap bmp = front ? CardRenderer.RenderFront(s, _settings) : CardRenderer.RenderBack(_settings))
                    return MobileLinkServer.ToJpeg(bmp, maxWidth);
            });
        }

        void IMobileLinkHost.PhotoReceived(Student s, string path, string device)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((Action)(() =>
            {
                if (IsDisposed) return;
                SetStatus("Photo from phone saved for " + Describe(s) + " (" + Path.GetFileName(path) + ")");
                UpdatePreview();   // the card on screen shows the new photo straight away
            }));
        }

        string IMobileLinkHost.RequestPrint(Student s, bool front, string device)
        {
            return OnUi(() =>
            {
                if (_mobilePrintRunning) return "A card is already printing on the PC. Wait until it is finished, then try again.";
                // Changing the selection under an open window (designer, settings, a print question…) would be confusing:
                // only the Mobile App page may stay open, as it is often left showing its QR code.
                bool busy = Application.OpenForms.Cast<Form>().Any(f => f != this && f.Visible && f.Modal && !(f is MobileLinkForm));
                if (busy) return "Card Studio is busy: a window is open on the PC. Close it there, then try again.";
                Student found = _students.FirstOrDefault(x => string.Equals((x.StudentId ?? "").Trim(), (s.StudentId ?? "").Trim(),
                                                                             StringComparison.OrdinalIgnoreCase));
                if (found == null) return "Student " + s.StudentId + " is no longer in the list loaded in Card Studio.";

                _mobilePrintRunning = true;
                BeginInvoke((Action)(() =>   // after the phone has its answer: Print Selected waits on questions at the PC
                {
                    try
                    {
                        if (IsDisposed || !SelectStudentById(found.StudentId)) return;
                        _sideTabs.SelectedIndex = front ? 0 : 1;
                        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                        Activate();
                        SetStatus("Print asked from the phone" + (string.IsNullOrEmpty(device) ? "" : " " + device) + ": " + Describe(found));
                        PrintSelected(string.IsNullOrEmpty(device) ? "the phone" : device);
                    }
                    finally
                    {
                        _mobilePrintRunning = false;
                    }
                }));
                return null;
            });
        }

        /// <summary>Runs <paramref name="work"/> on the UI thread and returns its result (throws while the window is closing).</summary>
        private T OnUi<T>(Func<T> work)
        {
            if (IsDisposed || !IsHandleCreated || Disposing) throw new InvalidOperationException("Card Studio is closing.");
            if (!InvokeRequired) return work();
            return (T)Invoke(work);
        }
    }
}
