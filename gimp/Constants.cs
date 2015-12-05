namespace Gimp
{
    using System;

    class Constants
    {
        public static readonly DateTime Never = new DateTime();
        public static readonly TimeSpan Interval7Days = new TimeSpan(7, 0, 0, 0);
        public static readonly TimeSpan Interval30Days = new TimeSpan(30, 0, 0, 0);
        public static readonly TimeSpan Interval90Days = new TimeSpan(90, 0, 0, 0);
        public static readonly TimeSpan Interval365Days = new TimeSpan(365, 0, 0, 0);

        public const string KeyWorkers = "Workers";
        public const string KeyUsername = "Username";
        public const string KeyPassword = "Password";
        public const string KeyUploadOffset = "UploadOffset";
        public const string KeyMinAssignmentCount = "MinAssignmentCount";

        public const string WorkTodoFileName = "worktodo.txt";
        public const string ResultsFileName = "results.txt";

        public const string StagingDir = "staging\\";
        public const string BackupDir = "backup\\";
        public const string LoggingDir = "logs\\";
        public const string WebLogsDir = "weblogs\\";

        public const string TxtExension = ".txt";

        public const string TestKey = "Test";
        public const string DblChkKey = "DoubleCheck";
    }
}
