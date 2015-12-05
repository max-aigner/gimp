using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Gimp
{
    class Program
    {
        private const string KeyWorkers = "Workers";
        private const string KeyUsername = "Username";
        private const string KeyPassword = "Password";

        private const string WorkTodoFileName = "worktodo.txt";
        private const string ResultsFileName = "results.txt";
        private const string StagingDir = "staging\\";
        private const string BackupDir = "backup\\";
        private const string LoggingDir = "logs\\";
        private const string WebLogsDir = "weblogs\\";
        private const string TxtExension = ".txt";
        private const string TestKey = "Test";
        private const string DblChkKey = "DoubleCheck";

        private static int MinAssignmentCount = 2;
        private static double LastCredit365 = 0;

        private static readonly DateTime Never = new DateTime();
        private static readonly List<string> folders = new List<string>();
        private static readonly List<Worker> workers = new List<Worker>();
        private static readonly TimeSpan AssignmentCheckInterval = new TimeSpan(0, 10, 11);
        private static readonly TimeSpan ResultCheckInterval = new TimeSpan(0, 1, 3);
        private static readonly TimeSpan Interval7Days = new TimeSpan(7, 0, 0, 0);
        private static readonly TimeSpan Interval30Days = new TimeSpan(30, 0, 0, 0);
        private static readonly TimeSpan Interval90Days = new TimeSpan(90, 0, 0, 0);
        private static readonly TimeSpan Interval365Days = new TimeSpan(365, 0, 0, 0);
        private static readonly int ResultUploadOffset = 58 * 60;
        private static string username;
        private static string password;

        public static void Main(string[] args)
        {
            StdOut("Start");

            ReadSettings();

            foreach (var folder in folders)
            {
                StdOut(string.Format("Init: Found worker directory {0}", folder));

                var worker = new Worker
                {
                    Directory = folder,
                    WorkTodoFileName = folder + "\\" + WorkTodoFileName,
                    WorkTodoTimestamp = Never,
                    ResultsFileName = folder + "\\" + ResultsFileName,
                    ResultsTimestamp = Never
                };

                workers.Add(worker);
            }

            Console.WriteLine();

            DateTime lastAssignmentCheck = Never;
            DateTime lastResultCheck = Never;
            bool resultsUploaded = false;

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

                if (offset >= ResultUploadOffset)
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

                Thread.Sleep(1000);
            }
        }

        /// <summary>
        /// Ensures each work todo file has at least the minimum number of
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

                    if (!trimmed.StartsWith(TestKey + "=") &&
                        !trimmed.StartsWith(DblChkKey + "="))
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
                        Gimps.PreferredWorkType.WorldRecordTests,
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
        /// Checks for results and writes all results lines to staging file.
        /// </summary>
        private static void CheckResults()
        {
            var stagingFileName = StagingDir + Guid.NewGuid().ToString() + TxtExension;

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

                    StdOut(string.Format("Results: {0} in folder {1}.", line, worker.Directory));

                    resultLines.Add(line);
                }

                if (resultLines.Any())
                {
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
            var stagedFiles = new List<string>(Directory.EnumerateFiles(StagingDir, "*.txt"));

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

                StdOut(string.Format("Upload: Successfully uploaded {0} line(s) from {1}:", resultLines.Count, file));

                var fileName = Path.GetFileName(file);

                File.Move(
                    StagingDir + fileName,
                    BackupDir + fileName);

                foreach (var line in resultLines)
                {
                    StdOut(string.Format("Upload: {0}", line));
                }
            }
        }

        /// <summary>
        /// Walks through the upload response HTML files in the web logs folder and
        /// calculates credit received for various periods. Prints a statistics line
        /// if the result is different from the last run.
        /// </summary>
        private static void CalculateStatistics()
        {
            var now = DateTime.UtcNow;
            var credit7 = 0.0;
            var credit30 = 0.0;
            var credit90 = 0.0;
            var credit365 = 0.0;


            var stagedFiles = new List<string>(Directory.EnumerateFiles(WebLogsDir, "*.upload.html"));

            foreach (var file in stagedFiles)
            {
                var interval = now - File.GetCreationTimeUtc(file);

                if (interval > Interval365Days)
                {
                    continue;
                }

                var credit = Gimps.ParseCpuCredit(File.ReadAllText(file));

                if (interval > Interval90Days)
                {
                    credit365 += credit;
                }

                if (interval > Interval30Days)
                {
                    credit365 += credit;
                    credit90 += credit;
                }

                if (interval > Interval7Days)
                {
                    credit365 += credit;
                    credit90 += credit;
                    credit30 += credit;
                }

                credit365 += credit;
                credit90 += credit;
                credit30 += credit;
                credit7 += credit;
            }

            if (credit365 == LastCredit365)
            {
                return;
            }

            StdOut(string.Format("Stats: 7: {0}, 30: {1}, 90: {2}, 365: {3}", credit7, credit30, credit90, credit365));
            Console.WriteLine();

            LastCredit365 = credit365;
        }

        /// <summary>
        /// Writes a message to stdout.
        /// </summary>
        /// <param name="display"></param>
        private static void StdOut(string display)
        {
            Console.WriteLine("{0} {1}", DateTime.Now.ToString("MMM dd  HH:mm:ss"), display);
        }

        /// <summary>
        /// Read settings from app config file.
        /// </summary>
        private static void ReadSettings()
        {
            var appSettings = ConfigurationManager.AppSettings;

            foreach (var key in appSettings.AllKeys)
            {
                var value = appSettings[key];

                switch (key)
                {
                    case KeyWorkers:
                        folders.AddRange(value.Split(';'));
                        break;

                    case KeyUsername:
                        username = value;
                        break;

                    case KeyPassword:
                        password = value;
                        break;
                }
            }
        }
    }
}
