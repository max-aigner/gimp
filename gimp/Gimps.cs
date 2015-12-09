namespace Gimp
{
    using System;
    using System.Net;
    using System.IO;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml;

    /// <summary>
    /// GIMPS specific methods for login, assignments, uploads, parsing etc.
    /// </summary>
    public class Gimps
    {
        private const string BeginBlockMarker = "<!--BEGIN_ASSIGNMENTS_BLOCK-->";
        private const string EndBlockMarker = "<!--END_ASSIGNMENTS_BLOCK-->";
        private const string UserValidationPhrase = "PROCESSING_VALIDATION:ASSIGNED TO ";
        private const string UserIdMarker = "name=\"was_logged_in_as\" value=\"";
        private const string CpuCredit = "CPU credit is ";
        private const string GhzDays = " GHz-days.";
        private const string Accept = "text/html, application/xhtml+xml, */*";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 6.3; WOW64; Trident/7.0; rv:11.0) like Gecko";

        /// <summary>
        /// Assignment types that can be requested from GIMPS server.
        /// </summary>
        public enum AssignmentType
        {
            /// <summary>
            /// Invalid assignment type.
            /// </summary>
            None = 0,

            /// <summary>
            /// World record Lucas-Lehmer (LL) test.
            /// </summary>
            WorldRecordTests = 102,

            /// <summary>
            /// Smallest available LL test.
            /// </summary>
            SmallestAvailableFirstTimeTests = 100,

            /// <summary>
            /// Double check (DC) test.
            /// </summary>
            DoubleCheckTests = 101,

            /// <summary>
            /// P1 factoring test.
            /// </summary>
            P1Factoring = 4,

            /// <summary>
            /// Trial factoring (TF) test.
            /// </summary>
            TrialFactoring = 2,

            /// <summary>
            /// Elliptic curve factoring test.
            /// </summary>
            ECMFactoring = 5,

            /// <summary>
            /// Large number LL test.
            /// </summary>
            HundredMillionDigitTest = 104
        }

        public enum ReportType
        {
            /// <summary>
            /// Overall
            /// </summary>
            All = 0,

            /// <summary>
            /// Trial factoring
            /// </summary>
            TrialFactoring = 1001,

            /// <summary>
            /// P-1 factoring
            /// </summary>
            P1Factoring = 1002,

            /// <summary>
            /// First LL testing
            /// </summary>
            FirstLlTesting = 1003,

            /// <summary>
            /// Double checking
            /// </summary>
            DoubleChecking = 1004,

            /// <summary>
            /// ECM on small Mersenne primes
            /// </summary>
            EcmMersenne = 1005,

            /// <summary>
            /// ECM on Fermat primes
            /// </summary>
            EcmFermat = 1006
        }

        /// <summary>
        /// Downloads customized report from GIMPS.
        /// </summary>
        /// <param name="logId"></param>
        /// <param name="teamFlag"></param>
        /// <param name="reportType"></param>
        /// <param name="rankLow"></param>
        /// <param name="rankHigh"></param>
        /// <param name="startDate"></param>
        /// <param name="endDate"></param>
        /// <returns></returns>
        public static string GetReport(
            string logId,
            bool teamFlag,
            ReportType reportType,
            int rankLow,
            int rankHigh,
            DateTime? startDate,
            DateTime? endDate)
        {
            var url =
                "http://www.mersenne.org/report_top_500_custom/?" +
                "team_flag=" + (teamFlag ? "1" : "0") +
                "&type=" + (int)reportType +
                "&rank_lo=" + rankLow +
                "&rank_hi=" + rankHigh +
                "&start_date=" + (startDate == null ? string.Empty : startDate.Value.ToString("yyyy-MM-dd")) +
                "&end_date=" + (endDate == null ? string.Empty : endDate.Value.ToString("yyyy-MM-dd"));

            var response = PerformGet(url);
            var method = reportType.ToString().ToLowerInvariant() + ".report";

            LogResponse(logId, method, response);

            var lines = ParseReport(response);

            File.WriteAllLines(Constants.ReportsDir + logId + "." + method + ".txt", lines.Select(x => x.ToString()));

            return response;
        }

        /// <summary>
        /// Upload results to GIMPS
        /// </summary>
        /// <param name="username">GIMPS user name</param>
        /// <param name="password">GIMPS password</param>
        /// <param name="fullFilename">The file containing .</param>
        /// <returns>Whether the upload succeeded.</returns>
        public static IEnumerable<ResultsLine> GetResults(
            string logId,
            string username,
            string password,
            bool excludeUnsuccessfulTf,
            bool excludeUnsuccessP1,
            bool excludeUnsuccessEcm,
            bool excludeLl,
            bool excludeFactorsFound,
            int? expStart,
            int? expEnd,
            int? limit)
        {
            var cookieJar = new CookieContainer();

            if (!Login(logId, username, password, cookieJar))
            {
                return new ResultsLine[0];
            }

            var response = GetResults(
                logId,
                cookieJar,
                excludeUnsuccessfulTf,
                excludeUnsuccessP1,
                excludeUnsuccessEcm,
                excludeLl,
                excludeFactorsFound,
                expStart,
                expEnd,
                limit);

            if (response == null)
            {
                return new ResultsLine[0];
            }

            return ParseResults(response);
        }

        /// <summary>
        /// Downloads results from GIMPS.
        /// </summary>
        /// <param name="logId"></param>
        /// <param name="teamFlag"></param>
        /// <param name="reportType"></param>
        /// <param name="rankLow"></param>
        /// <param name="rankHigh"></param>
        /// <param name="startDate"></param>
        /// <param name="endDate"></param>
        /// <returns></returns>
        private static string GetResults(
            string logId,
            CookieContainer cookieJar,
            bool excludeUnsuccessfulTf,
            bool excludeUnsuccessP1,
            bool excludeUnsuccessEcm,
            bool excludeLl,
            bool excludeFactorsFound,
            int? expStart,
            int? expEnd,
            int? limit)
        {
            var parameters = new List<string>();
            var url = "http://www.mersenne.org/results/?";

            if (excludeUnsuccessfulTf)
            {
                parameters.Add("extf=1");
            }

            if (excludeUnsuccessP1)
            {
                parameters.Add("exp1=1");
            }

            if (excludeUnsuccessEcm)
            {
                parameters.Add("execm=1");
            }

            if (excludeLl)
            {
                parameters.Add("exll=1");
            }

            if (excludeFactorsFound)
            {
                parameters.Add("exfac=1");
            }

            parameters.Add("exp_lo=" + (expStart.HasValue ? expStart.Value.ToString() : string.Empty));
            parameters.Add("exp_hi=" + (expEnd.HasValue ? expEnd.Value.ToString() : string.Empty));
            parameters.Add("limit=" + (limit.HasValue ? limit.Value.ToString() : string.Empty));

            url += string.Join("&", parameters);

            var response = PerformGet(url, cookieJar);

            LogResponse(logId, "results", response);

            return response;
        }

        /// <summary>
        /// Requests assignments from the GIMPS server.
        /// </summary>
        /// <param name="logId">ID string to use for creating the web log file name.</param>
        /// <param name="userName">User name for login.</param>
        /// <param name="password">Password for login.</param>
        /// <param name="cores">How many cores/GPUs to get assignments for.</param>
        /// <param name="assignmentsToGet">How many assignments to get per core/GPU.</param>
        /// <param name="assignmentType">The type of assignment requested.</param>
        /// <param name="expStart">Lower bound of exponent range (optional).</param>
        /// <param name="expEnd">Upper bound of exponent range (optional).</param>
        /// <returns>Enumeration of assignment strings.</returns>
        public static IEnumerable<string> GetAssignments(
            string logId,
            string userName,
            string password,
            int cores,
            int assignmentsToGet,
            AssignmentType assignmentType,
            int? expStart,
            int? expEnd)
        {
            string url =
                "http://www.mersenne.org/manual_assignment/misfit.php?uid=" + userName +
                "&user_password=" + password +
                "&cores=" + cores +
                "&num_to_get=" + assignmentsToGet +
                "&pref=" + (int)assignmentType +
                "&exp_lo=" + expStart +
                "&exp_hi=" + expEnd +
                "&B1=Get+Assignments";

            var response = PerformGet(url);

            if (response == null)
            {
                return null;
            }

            LogResponse(logId, "assign", response);

            return ParseAssignments(response);
        }

        /// <summary>
        /// Upload results to GIMPS
        /// </summary>
        /// <param name="username">GIMPS user name</param>
        /// <param name="password">GIMPS password</param>
        /// <param name="fullFilename">The file containing .</param>
        /// <returns>Whether the upload succeeded.</returns>
        public static bool UploadResults(
            string logId,
            string username,
            string password,
            IEnumerable<string> resultLines)
        {
            var cookieJar = new CookieContainer();

            if (!Login(logId, username, password, cookieJar))
            {
                return false;
            }

            var userId = GetUserId(logId, cookieJar);

            if (userId == null)
            {
                return false;
            }

            return UploadResults(logId, resultLines, userId, cookieJar);
        }

        /// <summary>
        /// Performs an HTTP GET from the given URL.
        /// </summary>
        /// <param name="url">Target URL.</param>
        /// <returns>Response as HTML string.</returns>
        private static string PerformGet(
            string url)
        {
            return PerformGet(url, null);
        }

        /// <summary>
        /// Performs an HTTP GET from the given URL.
        /// </summary>
        /// <param name="url">Target URL.</param>
        /// <returns>Response as HTML string.</returns>
        private static string PerformGet(
            string url,
            CookieContainer cookieJar)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = UserAgent;

            if (cookieJar != null)
            {
                request.CookieContainer = cookieJar;
            }

            try
            {
                var response = request.GetResponse();

                using (var sr = new StreamReader(response.GetResponseStream()))
                {
                    return sr.ReadToEnd();
                }
            }
            catch (IOException e)
            {
                Console.WriteLine(e.Message);
                return null;
            }
        }

        /// <summary>
        /// Perform login and store cookie.
        /// </summary>
        /// <param name="userName">The user name to use for logging in.</param>
        /// <param name="password">The password to use.</param>
        /// <param name="cookieJar">Cookie container that will receive cookie.</param>
        /// <returns>Whether the login succeeded.</returns>
        private static bool Login(
            string logId,
            string userName,
            string password,
            CookieContainer cookieJar)
        {
            var url = "http://www.mersenne.org/login.php";
            var postData = "user_login=" + userName + "&user_password=" + password;
            var response = string.Empty;
            var token = userName + "<br>logged in";
            var request = (HttpWebRequest)WebRequest.Create(url);

            request.Method = WebRequestMethods.Http.Post;
            request.Accept = Accept;
            request.Referer = url;
            request.ContentType = "application/x-www-form-urlencoded";
            request.UserAgent = UserAgent;
            request.CookieContainer = cookieJar;
            request.ContentLength = postData.Length;

            // Write the request
            using (var requestStream = request.GetRequestStream())
            {
                var requestWriter = new StreamWriter(requestStream);
                requestWriter.Write(postData);
                requestWriter.Flush();
            }

            // Read the response
            using (var responseStream = new StreamReader(request.GetResponse().GetResponseStream()))
            {
                response = responseStream.ReadToEnd();
            }

            LogResponse(logId, "login", response);

            return response.ToUpper().Contains(token.ToUpper());
        }

        /// <summary>
        /// Retrieve user ID from Manual Results page.
        /// </summary>
        /// <param name="cookieJar">Cookie container to use.</param>
        /// <returns>User ID or null.</returns>
        private static string GetUserId(
            string logId,
            CookieContainer cookieJar)
        {
            var url = "http://www.mersenne.org/manual_result/default.php";
            var response = string.Empty;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.CookieContainer = cookieJar;

            // Read the response
            using (var responseStream = new StreamReader(request.GetResponse().GetResponseStream()))
            {
                response = responseStream.ReadToEnd();
            }

            var beg = response.IndexOf(UserIdMarker);

            if (beg < 0)
            {
                return null;
            }

            beg += UserIdMarker.Length;

            var end = response.IndexOf('\"', beg);

            if (end < 0)
            {
                return null;
            }

            LogResponse(logId, "userid", response);

            return response.Substring(beg, end - beg);
        }

        /// <summary>
        /// Post assignment results to Manual Result page.
        /// </summary>
        /// <param name="fullFilename">File contaigning completed assignments.</param>
        /// <param name="userId">The user ID.</param>
        /// <param name="cookieJar">Cookie container to use.</param>
        /// <returns>HTML response string.</returns>
        private static bool UploadResults(
            string logId,
            IEnumerable<string> resultLines,
            string userId,
            CookieContainer cookieJar)
        {
            var url = "http://www.mersenne.org/manual_result/default.php";
            var response = string.Empty;
            var boundary = string.Format("---------------------------{0:x}", DateTime.UtcNow.Ticks).Substring(0, 40);
            var nl = Environment.NewLine;
            var propertyFormat = "Content-Disposition: form-data; name=\"{0}\"";
            var postData = string.Empty;

            postData += "--" + boundary + nl;
            postData += string.Format(propertyFormat, "data_file") + "; filename=\"\"" + nl;
            postData += "Content-Type: application/octet-stream" + nl;
            postData += nl;
            postData += nl;
            postData += "--" + boundary + nl;
            postData += string.Format(propertyFormat, "was_logged_in_as") + nl;
            postData += nl;
            postData += userId + nl;
            postData += "--" + boundary + nl;
            postData += string.Format(propertyFormat, "data") + nl;
            postData += nl;

            foreach (var line in resultLines)
            {
                postData += line + nl;
            }

            postData += "--" + boundary + "--" + nl;

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = WebRequestMethods.Http.Post;
            request.Accept = Accept;
            request.Referer = url;
            request.ContentType = "multipart/form-data; boundary=" + boundary;
            request.UserAgent = UserAgent;
            request.ContentLength = postData.Length;
            request.CookieContainer = cookieJar;

            // Write the request
            using (var requestStream = request.GetRequestStream())
            {
                var reqWriter = new StreamWriter(requestStream);
                reqWriter.Write(postData);
                reqWriter.Flush();
            }

            // Read the response
            using (var responseStream = new StreamReader(request.GetResponse().GetResponseStream()))
            {
                response = responseStream.ReadToEnd();
            }

            LogResponse(logId, "upload", response);

            const string Token = "Done processing:";

            return response.ToUpper().Contains(Token.ToUpper());
        }

        /// <summary>
        /// Extracts assignments from response to assignment request.
        /// </summary>
        /// <param name="response">The HTML response to process.</param>
        /// <returns>Enumeration of assignment strings.</returns>
        private static IEnumerable<string> ParseAssignments(string response)
        {
            if (!response.Contains(BeginBlockMarker) ||
                !response.Contains(EndBlockMarker) ||
                !response.Contains(UserValidationPhrase))
            {
                return null;
            }

            var beg = response.IndexOf(BeginBlockMarker) + BeginBlockMarker.Length;
            var end = response.IndexOf(EndBlockMarker);

            var assignments = response.Substring(beg, end - beg).Trim();

            return assignments.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Extracts assignment credit from upload response.
        /// </summary>
        /// <param name="response">HTML response to assignment upload.</param>
        /// <returns>Total assignment credit received for completing the uploaded assignments.</returns>
        public static decimal ParseCpuCredit(string response)
        {
            var total = 0m;
            var pos = 0;

            while (true)
            {
                pos = response.IndexOf(CpuCredit, pos);

                if (pos < 0)
                {
                    break;
                }

                var beg = pos + CpuCredit.Length;
                var end = response.IndexOf(GhzDays, beg);
                var len = end - beg;

                if (len <= 0)
                {
                    continue;
                }

                decimal credit;

                if (decimal.TryParse(response.Substring(beg, len), out credit))
                {
                    total += credit;
                }

                pos = end + GhzDays.Length;
            }

            return total;
        }

        /// <summary>
        /// Extracts report lines from report response.
        /// </summary>
        /// <param name="response">HTML response to report request.</param>
        /// <returns>Total assignment credit received for completing the uploaded assignments.</returns>
        public static IEnumerable<ReportLine> ParseReport(string response)
        {
            const string StartMarker = "|----- ----- ----- -----";
            const string Break = "<br>";
            const string EndMarker = "</pre>";
            const char BarMarker = '|';
            const string FiveFieldMarker = "Attempts Successes";

            var lines = new List<ReportLine>();
            var fiveFields = response.Contains(FiveFieldMarker);
            var beg = response.IndexOf(StartMarker);

            if (beg < 0)
            {
                return null;
            }

            var br = response.IndexOf(Break, beg + StartMarker.Length);

            while (br >= 0)
            {
                var cur = response.Substring(br + Break.Length);

                if (cur.StartsWith(EndMarker))
                {
                    break;
                }

                var bar = cur.IndexOf(BarMarker);
                var lineText = cur.Substring(0, bar);

                var line = ParseReportLine(lineText, fiveFields);

                lines.Add(line);

                br = response.IndexOf(Break, br + Break.Length);
            }

            return lines;
        }

        private static ReportLine ParseReportLine(string text, bool fiveFields)
        {
            const string LinkMarkerA = "<a target=\"_blank\" rel=\"nofollow\" href=\"";
            const string LinkMarkerB = "<a rel=\"nofollow\" target=\"_blank\" href=\"";
            const string BegNameMarker = "\">";
            const string EndNameMarker = "</a>";
            var line = new ReportLine();
            var i = 0;
            var beg = 0;
            var end = 0;
            var len = 0;

            // Parse the rank information from the beginning
            while (text[i] == ' ')
            {
                i++;
            }

            beg = i;

            while (text[i] != ' ')
            {
                i++;
            }

            len = i - beg;

            int number;

            if (int.TryParse(text.Substring(beg, len), out number))
            {
                line.Rank = number;
            }

            while (text[i] == ' ')
            {
                i++;
            }

            // Remove the rank text
            text = text.Substring(i);

            // Parse the rest of the line from the back
            i = text.Length - 1;

            // Some reports have three fields while others have five
            if (fiveFields)
            {
                while (text[i] == ' ')
                {
                    i--;
                }

                end = i;

                while (text[i] != ' ')
                {
                    i--;
                }

                beg = i + 1;
                len = end - i;

                if (int.TryParse(text.Substring(beg, len), out number))
                {
                    line.Successes = number;
                }

                while (text[i] == ' ')
                {
                    i--;
                }

                end = i;

                while (text[i] != ' ')
                {
                    i--;
                }

                beg = i + 1;
                len = end - i;

                if (int.TryParse(text.Substring(beg, len), out number))
                {
                    line.Attempts = number;
                }
            }

            while (text[i] == ' ')
            {
                i--;
            }

            end = i;

            while (text[i] != ' ')
            {
                i--;
            }

            beg = i + 1;
            len = end - i;

            decimal credit;

            if (decimal.TryParse(text.Substring(beg, len), out credit))
            {
                line.Credit = credit;
            }

            while (text[i] == ' ')
            {
                i--;
            }

            text = text.Substring(0, i + 1);

            if (text.StartsWith(LinkMarkerA) ||
                text.StartsWith(LinkMarkerB))
            {
                i = LinkMarkerA.Length;
                beg = text.IndexOf(BegNameMarker, LinkMarkerA.Length) + BegNameMarker.Length;
                end = text.IndexOf(EndNameMarker, beg);
                len = end - beg;
                text = text.Substring(beg, len);
            }

            line.Member = text;

            return line;
        }

        /// <summary>
        /// Extracts result lines from results response.
        /// </summary>
        /// <param name="response">HTML response to results request.</param>
        /// <returns>Enumeration of result lines.</returns>
        public static IEnumerable<ResultsLine> ParseResults(string response)
        {
            const string StartMarker = "<tbody>";
            const string EndMarker = "</tbody>";

            var lines = new List<ResultsLine>();
            var beg = response.IndexOf(StartMarker);
            var end = response.IndexOf(EndMarker);

            if (beg < 0 || end < 0 || beg > end)
            {
                return null;
            }

            end += EndMarker.Length;

            // Fix up ampersands so string can be parsed as XML fragment.
            var xml = response.Substring(beg, end - beg).Replace("&full", "&amp;full");
            var xmlDoc = new XmlDocument();

            try
            {
                xmlDoc.LoadXml(xml);
            }
            catch (XmlException e)
            {
                Console.WriteLine(e.Message);
                return new ResultsLine[0];
            }

            foreach (XmlNode row in xmlDoc.DocumentElement.ChildNodes)
            {
                var line = ParseResultsLine(row);

                lines.Add(line);
            }

            return lines;
        }

        private static ResultsLine ParseResultsLine(XmlNode row)
        {
            var line = new ResultsLine();
            var childNodes = row.ChildNodes;

            line.CpuName = childNodes[0].InnerText;
            long exponent;

            if (long.TryParse(childNodes[1].FirstChild.InnerText, out exponent))
            {
                line.Exponent = exponent;
            }

            line.ResultType = childNodes[2].InnerText;
            DateTime received;

            if (DateTime.TryParse(childNodes[3].InnerText, out received))
            {
                line.Received = received;
            }

            double days;

            if (double.TryParse(childNodes[4].InnerText, out days))
            {
                line.Age = TimeSpan.FromDays(days);
            }

            line.Result = childNodes[5].InnerText;
            decimal credit;

            if (decimal.TryParse(childNodes[6].InnerText, out credit))
            {
                line.Credit = credit;
            }

            return line;
        }

        /// <summary>
        /// Logs HTML response to file in web logs folder.
        /// </summary>
        /// <param name="logId">The log ID to use for the file name.</param>
        /// <param name="method">Name of requesting method.</param>
        /// <param name="response">The HTML response string to log.</param>
        private static void LogResponse(
            string logId,
            string method,
            string response)
        {
            File.WriteAllText(
                Constants.WebLogsDir + logId + "." + method + ".html",
                response);
        }
    }
}
