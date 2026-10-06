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

        /// <summary>Problems with the printer itself (offline, stopped, out of paper…), without the queue check.</summary>
        public static List<string> CheckPrinterOnly(string printerName)
        {
            var problems = new List<string>();
            try { CheckPrinter(printerName, problems); }
            catch (Exception ex) { problems.Add("(Could not read printer status from Windows: " + ex.Message + ")"); }
            return problems;
        }

        private static void CheckQueue(string printerName, List<string> problems)
        {
            List<PrintJobInfo> jobs = GetJobs(printerName);
            if (jobs.Count == 0) return;

            PrintJobInfo first = jobs[0];
            problems.Add(string.Format(
                "{0} job(s) already waiting in the print queue (first: \"{1}\"{2}). " +
                "The printer prints them first — click 'Print Queue...' and cancel the old jobs.",
                jobs.Count, first.Document,
                string.IsNullOrEmpty(first.RawStatus) ? "" : ", status: " + first.RawStatus));
        }

        /// <summary>Every job currently in the Windows spooler queue of this printer, oldest first.</summary>
        public static List<PrintJobInfo> GetJobs(string printerName)
        {
            var result = new List<PrintJobInfo>();
            using (var searcher = new ManagementObjectSearcher(
                "SELECT Name, JobId, Document, JobStatus, Owner, TotalPages, PagesPrinted, Size, TimeSubmitted FROM Win32_PrintJob"))
            using (ManagementObjectCollection jobs = searcher.Get())
            {
                foreach (ManagementObject job in jobs)
                {
                    using (job)
                    {
                        // Name = "<printer name>, <job id>"
                        string name = job["Name"] as string ?? "";
                        if (!name.StartsWith(printerName + ",", StringComparison.OrdinalIgnoreCase)) continue;

                        result.Add(new PrintJobInfo
                        {
                            JobId = ToInt(job["JobId"]),
                            Document = job["Document"] as string ?? "?",
                            RawStatus = job["JobStatus"] as string ?? "",
                            Owner = job["Owner"] as string ?? "",
                            TotalPages = ToInt(job["TotalPages"]),
                            PagesPrinted = ToInt(job["PagesPrinted"]),
                            SizeBytes = ToLong(job["Size"]),
                            Submitted = ToDate(job["TimeSubmitted"] as string)
                        });
                    }
                }
            }
            result.Sort((a, b) => a.JobId.CompareTo(b.JobId));
            return result;
        }

        /// <summary>Cancels (deletes) one job. Returns false when the job is already gone.</summary>
        public static bool CancelJob(string printerName, int jobId)
        {
            using (var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PrintJob WHERE JobId = " + jobId))
            using (ManagementObjectCollection jobs = searcher.Get())
            {
                foreach (ManagementObject job in jobs)
                {
                    using (job)
                    {
                        string name = job["Name"] as string ?? "";
                        if (!name.StartsWith(printerName + ",", StringComparison.OrdinalIgnoreCase)) continue;
                        job.Delete();
                        return true;
                    }
                }
            }
            return false;
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

        private static long ToLong(object value)
        {
            try { return value == null ? 0 : Convert.ToInt64(value); }
            catch { return 0; }
        }

        private static DateTime? ToDate(string cimDate)
        {
            if (string.IsNullOrEmpty(cimDate)) return null;
            try { return ManagementDateTimeConverter.ToDateTime(cimDate); }
            catch { return null; }
        }
    }

    /// <summary>One job in a printer's Windows spooler queue.</summary>
    internal class PrintJobInfo
    {
        public int JobId;
        public string Document;
        public string RawStatus;      // e.g. "Printing", "Paused", "Error | Offline"; empty = waiting
        public string Owner;
        public int TotalPages;
        public int PagesPrinted;
        public long SizeBytes;
        public DateTime? Submitted;

        public string StatusText
        {
            get { return string.IsNullOrEmpty(RawStatus) ? "Waiting" : RawStatus; }
        }
    }
}
