using System;
using System.Collections.Generic;
using System.Management;

namespace NPIC_CARD_APP.Printing
{
    /// <summary>
    /// Asks Windows (WMI) whether the printer is offline / in error and whether old jobs are stuck in its queue.
    /// doc.Print() only hands the job to the Windows spooler, so these problems never raise an exception —
    /// the card just doesn't print.
    /// </summary>
    internal static class PrinterStatus
    {
        public static List<string> Check(string printerName)
        {
            var problems = new List<string>();
            try
            {
                CheckPrinter(printerName, problems);
                CheckQueue(printerName, problems);
            }
            catch (Exception ex)
            {
                problems.Add("(Could not read printer status from Windows: " + ex.Message + ")");
            }
            return problems;
        }

        private static void CheckPrinter(string printerName, List<string> problems)
        {
            using (var searcher = new ManagementObjectSearcher(
                "SELECT Name, WorkOffline, PrinterStatus, DetectedErrorState FROM Win32_Printer"))
            using (ManagementObjectCollection printers = searcher.Get())
            {
                foreach (ManagementObject p in printers)
                {
                    using (p)
                    {
                        if (!string.Equals(p["Name"] as string, printerName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (p["WorkOffline"] is bool && (bool)p["WorkOffline"])
                            problems.Add("Windows shows the printer as OFFLINE (USB/Wi-Fi not connected, printer off, " +
                                         "or 'Use Printer Offline' is ticked in the print queue).");

                        int status = ToInt(p["PrinterStatus"]);
                        if (status == 6) problems.Add("The printer has stopped printing (PrinterStatus = Stopped).");
                        if (status == 7) problems.Add("The printer is offline (PrinterStatus = Offline).");

                        string error = ErrorText(ToInt(p["DetectedErrorState"]));
                        if (error != null) problems.Add("Printer reports: " + error + ".");
                    }
                }
            }
        }

        private static void CheckQueue(string printerName, List<string> problems)
        {
            int count = 0;
            string first = null;
            string firstStatus = null;

            using (var searcher = new ManagementObjectSearcher("SELECT Name, Document, JobStatus FROM Win32_PrintJob"))
            using (ManagementObjectCollection jobs = searcher.Get())
            {
                foreach (ManagementObject job in jobs)
                {
                    using (job)
                    {
                        // Name = "<printer name>, <job id>"
                        string name = job["Name"] as string ?? "";
                        if (!name.StartsWith(printerName + ",", StringComparison.OrdinalIgnoreCase)) continue;

                        count++;
                        if (first == null)
                        {
                            first = job["Document"] as string ?? "?";
                            firstStatus = job["JobStatus"] as string;
                        }
                    }
                }
            }

            if (count > 0)
            {
                problems.Add(string.Format(
                    "{0} job(s) already waiting in the print queue (first: \"{1}\"{2}). " +
                    "The printer prints them first — open the queue and cancel the old jobs.",
                    count, first, string.IsNullOrEmpty(firstStatus) ? "" : ", status: " + firstStatus));
            }
        }

        private static string ErrorText(int state)
        {
            switch (state)
            {
                case 3: return "low paper";
                case 4: return "no paper / no card tray";
                case 5: return "low ink";
                case 6: return "no ink";
                case 7: return "cover open";
                case 8: return "paper jam";
                case 9: return "offline";
                case 10: return "service requested";
                case 11: return "output tray full";
                default: return null;   // 0 Unknown, 1 Other, 2 No error
            }
        }

        private static int ToInt(object value)
        {
            try { return value == null ? 0 : Convert.ToInt32(value); }
            catch { return 0; }
        }
    }
}
