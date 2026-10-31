using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Main window ↔ Card Studio Admin (ADMIN_CARD), the administrator's tool that lists every PC running Card Studio on the
    /// network. The agent runs for the whole session; its commands arrive on agent threads and only touch the window through
    /// <see cref="OnUi{T}"/> / <see cref="OnUiWithin{T}"/>.
    /// </summary>
    public partial class MainForm : IAdminAgentHost
    {
        private AdminAgent _admin;

        // Print counts for the administrator, re-read only when the print log changes.
        private readonly object _adminCountsLock = new object();
        private DateTime _adminCountsStamp = DateTime.MinValue, _adminCountsDay = DateTime.MinValue;
        private int _adminPrintedToday, _adminPrintedTotal;
        private DateTime? _adminLastPrint;

        private void StartAdminAgent()
        {
            if (_admin == null) _admin = new AdminAgent(this);
            _admin.Start();   // never throws; a port already taken (another Windows user's Card Studio) only means no admin link
        }

        private void StopAdminAgent()
        {
            if (_admin != null) _admin.Dispose();
            _admin = null;
        }

        // ===================== IAdminAgentHost (agent threads) =====================

        void IAdminAgentHost.FillStatus(Dictionary<string, object> status)
        {
            MobileLinkServer mobile = _mobile;
            bool running = mobile != null && mobile.Running;
            status["mobileRunning"] = running;
            status["mobilePort"] = running ? mobile.Port : 0;
            status["phones"] = running ? mobile.ConnectedPhones().Take(20).Select(p => AdminProtocol.Clip(p, 60)).ToList() : new List<string>();
            FillPrintCounts(status);

            object[] ui = OnUiWithin(() => new object[]
            {
                _students.Count,
                _students.Count == 0 ? "" : _source ?? "",
                _settings.PrinterName ?? "",
                MobilePrintProblem()
            }, 1500, null);
            if (ui == null)
            {
                status["busy"] = "Card Studio is busy: its window does not answer.";
                return;
            }
            status["students"] = ui[0];
            status["source"] = AdminProtocol.Clip((string)ui[1], 120);
            status["printer"] = AdminProtocol.Clip((string)ui[2], 120);
            status["busy"] = (string)ui[3] ?? "";
        }

        private void FillPrintCounts(Dictionary<string, object> status)
        {
            lock (_adminCountsLock)
            {
                try
                {
                    string file = PrintHistory.FilePath;
                    DateTime stamp = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
                    if (stamp != _adminCountsStamp || _adminCountsDay != DateTime.Today)
                    {
                        List<PrintRecord> all = PrintHistory.Load();
                        _adminPrintedToday = all.Count(r => r.PrintedAt.Date == DateTime.Today);
                        _adminPrintedTotal = all.Count;
                        _adminLastPrint = all.Count == 0 ? (DateTime?)null : all.Max(r => r.PrintedAt);
                        _adminCountsStamp = stamp;
                        _adminCountsDay = DateTime.Today;
                    }
                }
                catch (Exception) { /* history unreadable: keep the last counts */ }
                status["printedToday"] = _adminPrintedToday;
                status["printedTotal"] = _adminPrintedTotal;
                status["lastPrint"] = _adminLastPrint == null ? "" :
                    _adminLastPrint.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
        }

        string IAdminAgentHost.ShowAdminMessage(string text)
        {
            return OnUi(() =>
            {
                SetStatus("Message from the administrator: " + text);
                _bell.Ring();
                // A box the user cannot miss, unless something is open on the PC (a print question, the designer…):
                // then the bell keeps the message. Shown after the reply, so the administrator does not wait for OK.
                if (!AnyModalOpen())
                    BeginInvoke((Action)(() =>
                    {
                        if (IsDisposed) return;
                        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                        AppDialog.Show(this, text, "Message from the administrator", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                return (string)null;
            });
        }

        string IAdminAgentHost.AdminCheckForUpdates()
        {
            return OnUi(() =>
            {
                if (_mobilePrintRunning || AnyModalOpen())
                {
                    SetStatus("Update requested by the administrator: press Ctrl+U to check for updates.");
                    return "Card Studio is busy on that PC (a window is open or a card is printing): the user was asked to check for updates.";
                }
                BeginInvoke((Action)(() => { if (!IsDisposed) ShowUpdates(); }));
                return (string)null;
            });
        }

        void IAdminAgentHost.SerialInstalled()
        {
            OnUi(() =>
            {
                UpdateTitle();
                SetStatus("License renewed by the administrator: " + LicenseService.Check().Describe());
                return true;
            });
        }

        string IAdminAgentHost.AdminRevokePhones()
        {
            return OnUi(() =>
            {
                if (Application.OpenForms.OfType<MobileLinkForm>().Any())
                    return "The Mobile App page is open on that PC. Close it first, or make the new code there (New Code).";
                string token = MobileLinkServer.NewToken();
                _settings.MobileLinkToken = token;
                SaveSettings(false);
                if (_mobile != null && _mobile.Running) _mobile.ChangeToken(token);
                SetStatus("Phones disconnected by the administrator: pair them again on the Mobile App page.");
                return (string)null;
            });
        }

        private bool AnyModalOpen()
        {
            return Application.OpenForms.Cast<Form>().Any(f => f != this && f.Visible && f.Modal);
        }

        /// <summary>
        /// Like <see cref="OnUi{T}"/>, but gives up after <paramref name="milliseconds"/> and returns <paramref name="fallback"/>
        /// (the work still runs later): only for read-only work.
        /// </summary>
        private T OnUiWithin<T>(Func<T> work, int milliseconds, T fallback)
        {
            if (IsDisposed || !IsHandleCreated || Disposing) throw new InvalidOperationException("Card Studio is closing.");
            if (!InvokeRequired) return work();
            IAsyncResult r = BeginInvoke(work);
            if (!r.AsyncWaitHandle.WaitOne(milliseconds)) return fallback;
            return (T)EndInvoke(r);
        }
    }
}
