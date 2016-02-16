namespace Gimp
{
    using Microsoft.Win32;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;

    class Program
    {
        private static readonly List<string> folders = new List<string>();
        private static readonly List<Worker> workers = new List<Worker>();
        private static readonly TimeSpan AssignmentCheckInterval = new TimeSpan(0, 10, 11);
        private static readonly TimeSpan ResultCheckInterval = new TimeSpan(0, 1, 3);
        private static readonly TimeSpan UploadCheckInterval = new TimeSpan(0, 2, 7);
        private static readonly TimeSpan StatisticsInterval = new TimeSpan(0, 20, 11);

        /// <summary>
        /// Password for encrypting the user's password.
        /// Use Misfit password so config files stay compatible.
        /// </summary>
        private static readonly string CryptoPassword = Environment.MachineName.ToUpper() + "]y6P41L[" + Environment.UserName.ToUpper();

        private static string username;
        private static string password;
        private static int MinAssignmentCount = 2;
        private static int? MinExponent = null;
        private static int? MaxExponent = null;
        private static Gimps.AssignmentType AssignmentType = Gimps.AssignmentType.WorldRecordTests;
        private static TimeSpan UploadOffset = new TimeSpan(0, 58, 0);

        /// <summary>
        /// Report download is disabled by default (unless overridden in config) by
        /// setting it to an offset gretaer than 1 hour.
        /// </summary>
        private static TimeSpan ReportOffset = new TimeSpan(1, 12, 0);

        /// <summary>
        /// Last calculated credit amount.
        /// </summary>
        private static int LastCreditHashCode = 0;

        private static Output monitor = new Output();

        public static void Main(string[] args)
        {
            if (args.Length >= 1)
            {
                switch (args[0])
                {
                    case "-?":
                    case "/?":
                        Usage();
                        break;

                    case "-p":
                    case "/p":
                        if (args.Length < 2)
                        {
                            Usage();
                        }
                        else
                        {
                            SetPassword(args[1]);
                        }
                        break;
                }

                return;
            }

            monitor.PrintStatus(
                Output.Facility.Start,
                string.Empty);

            ReadSettings();

            foreach (var folder in folders)
            {
                monitor.PrintStatus(
                    Output.Facility.Init,
                    string.Format("Found worker directory {0}", folder));

                var worker = new Worker
                {
                    Directory = folder,
                    WorkTodoFileName = folder + "\\" + Constants.WorkTodoFileName,
                    WorkTodoTimestamp = Constants.Never,
                    ResultsFileName = folder + "\\" + Constants.ResultsFileName,
                    ResultsTimestamp = Constants.Never
                };

                workers.Add(worker);
            }

            Console.WriteLine();

            var lastAssignmentCheck = Constants.Never;
            var lastResultCheck = Constants.Never;
            var lastUploadCheck = Constants.Never;
            var lastStatisticsCheck = Constants.Never;
            var reportsDownloaded = false;

            while (true)
            {
                var now = DateTime.UtcNow;

                if (now - lastAssignmentCheck >= AssignmentCheckInterval)
                {
                    CheckAssignments();
                    lastAssignmentCheck = now;
                }

                if (now - lastResultCheck >= ResultCheckInterval)
                {
                    if (CheckResults())
                    {
                        lastUploadCheck = Constants.Never;
                    }

                    lastResultCheck = now;
                }

                if (now - lastUploadCheck >= UploadCheckInterval)
                {
                    if (UploadCheck())
                    {
                        lastStatisticsCheck = Constants.Never;
                    }

                    lastUploadCheck = now;
                }

                if (now - lastStatisticsCheck >= StatisticsInterval)
                {
                    CalculateStatistics();
                    lastStatisticsCheck = now;
                }

                var hourlyOffset = new TimeSpan(0, now.Minute, now.Second);

                if (hourlyOffset >= ReportOffset)
                {
                    if (!reportsDownloaded)
                    {
                        DownloadReports();
                        reportsDownloaded = true;
                    }
                }
                else
                {
                    reportsDownloaded = false;
                }

                Thread.Sleep(1000);
            }
        }

        /// <summary>
        /// Ensures each work todo file has the minimum number of
        /// assignments.
        /// </summary>
        private static void CheckAssignments()
        {
            foreach (var worker in workers)
            {
                var assignmentLines = new List<string>();

                if (!File.Exists(worker.WorkTodoFileName))
                {
                    using (File.CreateText(worker.WorkTodoFileName))
                    {
                    }
                }

                var timestamp = File.GetLastWriteTimeUtc(worker.WorkTodoFileName);

                if (timestamp == worker.WorkTodoTimestamp)
                {
                    continue;
                }

                var lines = File.ReadAllLines(worker.WorkTodoFileName);

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var trimmed = line.Trim();

                    if (!trimmed.StartsWith(Constants.TestKey + "=") &&
                        !trimmed.StartsWith(Constants.DblChkKey + "="))
                    {
                        continue;
                    }

                    assignmentLines.Add(line);
                }

                if (assignmentLines.Count < MinAssignmentCount)
                {
                    var assignments = new List<string>(Gimps.GetAssignments(
                        Guid.NewGuid().ToString(),
                        username,
                        password,
                        MinAssignmentCount - assignmentLines.Count,
                        1,
                        AssignmentType,
                        MinExponent,
                        MaxExponent));

                    if (assignments != null)
                    {
                        File.AppendAllLines(worker.WorkTodoFileName, assignments);

                        monitor.PrintStatus(
                            Output.Facility.Assign,
                            string.Format("Added {0} line(s) to {1}:", assignments.Count, worker.WorkTodoFileName));

                        foreach (var line in assignments)
                        {
                            monitor.PrintStatus(
                                Output.Facility.Assign,
                                line);
                        }

                        timestamp = File.GetLastWriteTimeUtc(worker.WorkTodoFileName);
                    }
                }

                worker.WorkTodoTimestamp = timestamp;
            }
        }

        /// <summary>
        /// Checks for results and writes results lines to staging file.
        /// </summary>
        /// <returns>Value indicating whether any results were found.</returns>
        private static bool CheckResults()
        {
            var resultsFound = false;
            var stagingFileName = Constants.StagingDir + Guid.NewGuid().ToString() + Constants.TxtExtension;

            foreach (var worker in workers)
            {
                var resultLines = new List<string>();

                if (!File.Exists(worker.ResultsFileName))
                {
                    using (File.CreateText(worker.ResultsFileName))
                    {
                    }
                }

                var timestamp = File.GetLastWriteTimeUtc(worker.ResultsFileName);

                if (timestamp == worker.ResultsTimestamp)
                {
                    continue;
                }

                var lines = File.ReadAllLines(worker.ResultsFileName);

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    resultLines.Add(line);
                }

                if (resultLines.Any())
                {
                    monitor.PrintStatus(
                        Output.Facility.Result,
                        string.Format("Found {0} line(s) in folder {1}:", resultLines.Count, worker.Directory));

                    foreach (var line in resultLines)
                    {
                        monitor.PrintStatus(
                            Output.Facility.Result,
                            line);
                    }

                    File.AppendAllLines(
                            stagingFileName,
                            resultLines);

                    File.WriteAllText(worker.ResultsFileName, string.Empty);
                    timestamp = File.GetLastWriteTimeUtc(worker.ResultsFileName);

                    resultsFound = true;
                }

                worker.ResultsTimestamp = timestamp;
            }

            return resultsFound;
        }

        /// <summary>
        /// Checks whether there are any files in staging that need to be uploaded
        /// to GIMPS. Uploads files and moves them to the backup folder.
        /// </summary>
        /// <returns>Value indicating whether any results have been uploaded.</returns>
        private static bool UploadCheck()
        {
            var resultsUploaded = false;
            var stagedFiles = new List<string>(Directory.EnumerateFiles(Constants.StagingDir, "*.txt"));

            foreach (var file in stagedFiles)
            {
                var resultLines = new List<string>();
                var lines = File.ReadAllLines(file);

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    resultLines.Add(line);
                }

                if (resultLines.Count == 0)
                {
                    continue;
                }

                var logId = Path.GetFileNameWithoutExtension(file);

                if (!Gimps.UploadResults(
                        logId,
                        username,
                        password,
                        resultLines))
                {
                    monitor.PrintStatus(
                        Output.Facility.Upload,
                        string.Format("Error uploading {0}", file));
                    continue;
                }

                resultsUploaded = true;

                var fileName = Path.GetFileName(file);

                File.Move(
                    Constants.StagingDir + fileName,
                    Constants.BackupDir + fileName);

                monitor.PrintStatus(
                    Output.Facility.Upload,
                    string.Format("Uploaded {0} line(s) from {1}:", resultLines.Count, file));

                foreach (var line in resultLines)
                {
                    monitor.PrintStatus(
                        Output.Facility.Upload,
                        line);
                }
            }

            return resultsUploaded;
        }

        /// <summary>
        /// Requests a report for all LL assignment results and calculates
        /// credit received for various periods. Outputs statistics line
        /// if new result is different from last run.
        /// </summary>
        private static void CalculateStatistics()
        {
            var credit1 = 0m;
            var credit7 = 0m;
            var credit30 = 0m;
            var credit90 = 0m;
            var credit365 = 0m;
            var creditTotal = 0m;
            var now = DateTime.UtcNow;
            var lines = Gimps.GetResults(
                Guid.NewGuid().ToString(),
                username,
                password,
                true,
                true,
                true,
                false,
                true,
                null,
                null,
                500);

            foreach (var line in lines)
            {
                if (line.ResultType != Constants.ResultTypeLl)
                {
                    continue;
                }

                var interval = now - line.Received;
                var credit = line.Credit;

                creditTotal += credit;

                if (interval <= Constants.Interval365Days)
                {
                    credit365 += credit;
                }

                if (interval <= Constants.Interval90Days)
                {
                    credit90 += credit;
                }

                if (interval <= Constants.Interval30Days)
                {
                    credit30 += credit;
                }

                if (interval <= Constants.Interval7Days)
                {
                    credit7 += credit;
                }

                if (interval <= Constants.Interval1Day)
                {
                    credit1 += credit;
                }
            }

            if (creditTotal == 0)
            {
                return;
            }

            var creditHashCode = credit1.GetHashCode()
                ^ credit7.GetHashCode()
                ^ credit30.GetHashCode()
                ^ credit90.GetHashCode()
                ^ credit365.GetHashCode()
                ^ creditTotal.GetHashCode();

            if (creditHashCode == LastCreditHashCode)
            {
                return;
            }

            monitor.PrintStatus(
                Output.Facility.Stats,
                string.Format("1: {0:F3}, 7: {1:F3}, 30: {2:F3}, 90: {3:F3}, 365: {4:F3}, total: {5:F3}", credit1, credit7, credit30, credit90, credit365, creditTotal));

            LastCreditHashCode = creditHashCode;
        }

        /// <summary>
        /// Walks through the upload response HTML files in the web logs folder and
        /// calculates credit received for various periods. Outputs statistics line
        /// if result is different from last run.
        /// </summary>
        private static void CalculateStatisticsLocal()
        {
            var credit1 = 0m;
            var credit7 = 0m;
            var credit30 = 0m;
            var credit90 = 0m;
            var credit365 = 0m;
            var creditTotal = 0m;
            var now = DateTime.UtcNow;
            var uploadFiles = new List<string>(Directory.EnumerateFiles(Constants.WebLogsDir, "*.upload.html"));

            foreach (var file in uploadFiles)
            {
                var interval = now - File.GetCreationTimeUtc(file);

                var credit = Gimps.ParseCpuCredit(File.ReadAllText(file));

                creditTotal += credit;

                if (interval <= Constants.Interval365Days)
                {
                    credit365 += credit;
                }

                if (interval <= Constants.Interval90Days)
                {
                    credit90 += credit;
                }

                if (interval <= Constants.Interval30Days)
                {
                    credit30 += credit;
                }

                if (interval <= Constants.Interval7Days)
                {
                    credit7 += credit;
                }

                if (interval <= Constants.Interval1Day)
                {
                    credit1 += credit;
                }
            }

            var creditHashCode = credit1.GetHashCode()
                ^ credit7.GetHashCode()
                ^ credit30.GetHashCode()
                ^ credit90.GetHashCode()
                ^ credit365.GetHashCode()
                ^ creditTotal.GetHashCode();

            if (creditHashCode == LastCreditHashCode)
            {
                return;
            }

            monitor.PrintStatus(
                Output.Facility.Stats,
                string.Format("1: {0}, 7: {1}, 30: {2}, 90: {3}, 365: {4}, total: {5}", credit1, credit7, credit30, credit90, credit365, creditTotal));

            LastCreditHashCode = creditHashCode;
        }

        /// <summary>
        /// Downloads reports from the GIMPS website.
        /// </summary>
        private static void DownloadReports()
        {
            const int rankLo = 1;
            const int rankHi = 500;

            var now = DateTime.UtcNow;
            var logDate = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);

            if (now.Minute > 1)
            {
                logDate = logDate + new TimeSpan(1, 0, 0);
            }

            var logId = logDate.ToString("yyyy-MM-dd-HH");
            var startDate = Constants.GimpsStart;

            Gimps.GetReport(logId, false, Gimps.ReportType.All, rankLo, rankHi, startDate, null);
            Gimps.GetReport(logId, false, Gimps.ReportType.TrialFactoring, rankLo, rankHi, startDate, null);
            Gimps.GetReport(logId, false, Gimps.ReportType.P1Factoring, rankLo, rankHi, startDate, null);
            Gimps.GetReport(logId, false, Gimps.ReportType.FirstLlTesting, rankLo, rankHi, startDate, null);
            Gimps.GetReport(logId, false, Gimps.ReportType.DoubleChecking, rankLo, rankHi, startDate, null);
            Gimps.GetReport(logId, false, Gimps.ReportType.EcmMersenne, rankLo, rankHi, startDate, null);
            Gimps.GetReport(logId, false, Gimps.ReportType.EcmFermat, rankLo, rankHi, startDate, null);
        }

        /// <summary>
        /// Reads settings from app config file.
        /// </summary>
        private static void ReadSettings()
        {
            var appSettings = new IniFile(Constants.IniFileName);
            var number = 0;

            foreach (var key in appSettings.AllKeys)
            {
                var value = appSettings[key];

                switch (key)
                {
                    case Constants.KeyWorker:
                        var values = appSettings.GetValues(key);
                        folders.AddRange(values);
                        break;

                    case Constants.KeyUsername:
                        username = value;
                        break;

                    case Constants.KeyPassword:
                        password = Crypto.Decrypt(value, CryptoPassword);
                        break;

                    case Constants.KeyMinAssignmentCount:
                        if (int.TryParse(value, out number))
                        {
                            MinAssignmentCount = number;
                        }
                        break;

                    case Constants.KeyMinExponent:
                        if (int.TryParse(value, out number))
                        {
                            MinExponent = number;
                        }
                        break;

                    case Constants.KeyMaxExponent:
                        if (int.TryParse(value, out number))
                        {
                            MaxExponent = number;
                        }
                        break;

                    case Constants.KeyUploadOffset:
                        if (int.TryParse(value, out number))
                        {
                            UploadOffset = new TimeSpan(0, number, 0);
                        }
                        break;

                    case Constants.KeyReportOffset:
                        if (int.TryParse(value, out number))
                        {
                            ReportOffset = new TimeSpan(0, number, 0);
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Outputs usage information.
        /// </summary>
        private static void Usage()
        {
            Console.WriteLine("Automated GIMPS assignment and results management.");
            Console.WriteLine("Normal operation:   gimp");
            Console.WriteLine("Usage hints:        gimp -?");
            Console.WriteLine("Set GIMPS password: gimp -p password");
        }

        /// <summary>
        /// Encrypts password and saves it in settings file.
        /// </summary>
        /// <param name="password"></param>
        private static void SetPassword(string password)
        {
            var appSettings = new IniFile(Constants.IniFileName);
            var enc = Crypto.Encrypt(password, CryptoPassword);

            appSettings.Set(Constants.KeyPassword, enc);

            appSettings.Refresh();

            Console.WriteLine("Password saved");
        }

        /// <summary>
        /// Retrieves string used as crypto salt.
        /// </summary>
        /// <returns>Crypto salt.</returns>
        public static byte[] CryptoSalt()
        {
            const string keyName = "UserGuid";
            var rkey = Registry.CurrentUser;

            var myGuid = (string)rkey.GetValue(keyName, string.Empty);

            if (string.IsNullOrWhiteSpace(myGuid))
            {
                myGuid = Guid.NewGuid().ToString();
                rkey.SetValue(keyName, myGuid);
            }

            var bytes = new byte[myGuid.Length * sizeof(char)];
            Buffer.BlockCopy(myGuid.ToCharArray(), 0, bytes, 0, bytes.Length);

            return bytes;
        }
    }
}
