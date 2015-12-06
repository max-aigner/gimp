namespace Gimp
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.IO;
    using System.Linq;
    using System.Threading;

    class Program
    {
        private static readonly List<string> folders = new List<string>();
        private static readonly List<Worker> workers = new List<Worker>();
        private static readonly TimeSpan AssignmentCheckInterval = new TimeSpan(0, 10, 11);
        private static readonly TimeSpan ResultCheckInterval = new TimeSpan(0, 1, 3);

        private static string username;
        private static string password;
        private static int UploadOffset = 58 * 60;
        private static int MinAssignmentCount = 2;
        private static int ReportOffset = 12 * 60;

        /// <summary>
        /// Last calculated credit amount.
        /// </summary>
        private static double LastCreditTotal = 0;

        public static void Main(string[] args)
        {
            var response = File.ReadAllText(Constants.WebLogsDir + "2015-12-06-08.all.report.html");
            Gimps.ParseReport(response);

            // return;

            StdOut("Start");
            Console.WriteLine();

            ReadSettings();

            foreach (var folder in folders)
            {
                StdOut(string.Format("Init: Found worker directory {0}", folder));

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

            DateTime lastAssignmentCheck = Constants.Never;
            DateTime lastResultCheck = Constants.Never;
            bool resultsUploaded = false;
            bool reportsDownloaded = false;

            CalculateStatistics();

            while (true)
            {
                DateTime now = DateTime.UtcNow;

                if (now - lastAssignmentCheck >= AssignmentCheckInterval)
                {
                    CheckAssignments();
                    lastAssignmentCheck = now;
                }

                if (now - lastResultCheck >= ResultCheckInterval)
                {
                    CheckResults();
                    lastResultCheck = now;
                }

                var offset = now.Minute * 60 + now.Second;

                if (offset >= UploadOffset)
                {
                    if (!resultsUploaded)
                    {
                        UploadCheck();
                        resultsUploaded = true;

                        CalculateStatistics();
                    }
                }
                else
                {
                    resultsUploaded = false;
                }

                if (offset >= ReportOffset)
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
                    List<string> assignments = new List<string>(Gimps.GetAssignments(
                        Guid.NewGuid().ToString(),
                        username,
                        password,
                        MinAssignmentCount - assignmentLines.Count,
                        1,
                        Gimps.AssignmentType.WorldRecordTests,
                        null,
                        null));

                    if (assignments != null)
                    {
                        File.AppendAllLines(worker.WorkTodoFileName, assignments);

                        StdOut(string.Format("Assign: Added {0} line(s) to {1}:", assignments.Count, worker.WorkTodoFileName));

                        foreach (var line in assignments)
                        {
                            StdOut(string.Format("Assign: {0}", line));
                        }

                        Console.WriteLine();

                        timestamp = File.GetLastWriteTimeUtc(worker.WorkTodoFileName);
                    }
                }

                worker.WorkTodoTimestamp = timestamp;
            }
        }

        /// <summary>
        /// Checks for results and writes results lines to staging file.
        /// </summary>
        private static void CheckResults()
        {
            var stagingFileName = Constants.StagingDir + Guid.NewGuid().ToString() + Constants.TxtExension;

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
                    StdOut(string.Format("Result: Found {0} lines in folder {1}:", resultLines.Count, worker.Directory));

                    foreach (var line in resultLines)
                    {
                        StdOut(string.Format("Result: {0}", line));
                    }

                    Console.WriteLine();

                    File.AppendAllLines(
                            stagingFileName,
                            resultLines);

                    File.WriteAllText(worker.ResultsFileName, string.Empty);
                    timestamp = File.GetLastWriteTimeUtc(worker.ResultsFileName);
                }

                worker.ResultsTimestamp = timestamp;
            }
        }

        /// <summary>
        /// Checks whether there are any files in staging that need to be uploaded
        /// to GIMPS. Uploads files and moves them to the backup folder.
        /// </summary>
        private static void UploadCheck()
        {
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
                    StdOut(string.Format("Upload: Error uploading {0}", file));
                    continue;
                }

                var fileName = Path.GetFileName(file);

                File.Move(
                    Constants.StagingDir + fileName,
                    Constants.BackupDir + fileName);

                StdOut(string.Format("Upload: Uploaded {0} line(s) from {1}:", resultLines.Count, file));

                foreach (var line in resultLines)
                {
                    StdOut(string.Format("Upload: {0}", line));
                }

                Console.WriteLine();
            }
        }

        /// <summary>
        /// Walks through the upload response HTML files in the web logs folder and
        /// calculates credit received for various periods. Outputs statistics line
        /// if result is different from last run.
        /// </summary>
        private static void CalculateStatistics()
        {
            var credit1 = 0.0;
            var credit7 = 0.0;
            var credit30 = 0.0;
            var credit90 = 0.0;
            var credit365 = 0.0;
            var creditTotal = 0.0;
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

            if (creditTotal == LastCreditTotal)
            {
                return;
            }

            StdOut(string.Format("Stats:  1: {0}, 7: {1}, 30: {2}, 90: {3}, 365: {4}, total: {5}", credit1, credit7, credit30, credit90, credit365, creditTotal));
            Console.WriteLine();

            LastCreditTotal = creditTotal;
        }

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
        /// Writes a message to stdout.
        /// </summary>
        /// <param name="display"></param>
        private static void StdOut(string display)
        {
            Console.WriteLine("{0} {1}", DateTime.Now.ToString("MMM dd HH:mm:ss"), display);
        }

        /// <summary>
        /// Reads settings from app config file.
        /// </summary>
        private static void ReadSettings()
        {
            var appSettings = ConfigurationManager.AppSettings;
            var number = 0;

            foreach (var key in appSettings.AllKeys)
            {
                var value = appSettings[key];

                switch (key)
                {
                    case Constants.KeyWorkers:
                        folders.AddRange(value.Split(';'));
                        break;

                    case Constants.KeyUsername:
                        username = value;
                        break;

                    case Constants.KeyPassword:
                        password = value;
                        break;

                    case Constants.KeyUploadOffset:
                        if (int.TryParse(value, out number))
                        {
                            UploadOffset = number * 60;
                        }
                        break;

                    case Constants.KeyMinAssignmentCount:
                        if (int.TryParse(value, out number))
                        {
                            MinAssignmentCount = number;
                        }
                        break;

                    case Constants.KeyReportOffset:
                        if (int.TryParse(value, out number))
                        {
                            ReportOffset = number * 60;
                        }
                        break;
                }
            }
        }
    }
}
