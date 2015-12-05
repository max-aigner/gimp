namespace Gimp
{
    using System;
    using System.Net;
    using System.IO;
    using System.Collections.Generic;

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
        public enum PreferredWorkType
        {
            /// <summary>
            /// Invalid assignment type.
            /// </summary>
            None = 0,

            /// <summary>
            /// World record Lucas-Lehmer LL test assignment.
            /// </summary>
            WorldRecordTests = 102,

            /// <summary>
            /// Smallest available LL test assignment.
            /// </summary>
            SmallestAvailableFirstTimeTests = 100,

            /// <summary>
            /// Double check (DC) test assignment.
            /// </summary>
            DoubleCheckTests = 101,

            /// <summary>
            /// P1 factoring test assignment.
            /// </summary>
            P1Factoring = 4,

            /// <summary>
            /// Trial factoring (TF) test assignment.
            /// </summary>
            TrialFactoring = 2,

            /// <summary>
            /// Elliptic curve factoring test assignment.
            /// </summary>
            ECMFactoring = 5,

            /// <summary>
            /// Large number LL test assignment.
            /// </summary>
            HundredMillionDigitTest = 104
        }

        /// <summary>
        /// Requests assignments from the GIMPS server.
        /// </summary>
        /// <param name="logId">ID string to use for creating the web log file name.</param>
        /// <param name="userName">User name for login.</param>
        /// <param name="password">Password for login.</param>
        /// <param name="cores">How many cores/GPUs to get assignments for.</param>
        /// <param name="assignmentsToGet">How many assignments to get per core/GPU.</param>
        /// <param name="workType">The type of work requested.</param>
        /// <param name="expStart">Lower bound of exponent range (optional).</param>
        /// <param name="expEnd">Upper bound of exponent range (optional).</param>
        /// <returns>Enumeration of assignment strings.</returns>
        public static IEnumerable<string> GetAssignments(
            string logId,
            string userName,
            string password,
            int cores,
            int assignmentsToGet,
            PreferredWorkType workType,
            int? expStart,
            int? expEnd)
        {
            string url =
                "http://www.mersenne.org/manual_assignment/misfit.php?uid=" + userName +
                "&user_password=" + password +
                "&cores=" + cores +
                "&num_to_get=" + assignmentsToGet +
                "&pref=" + (int)workType +
                "&exp_lo=" + expStart +
                "&exp_hi=" + expEnd +
                "&B1=Get+Assignments";

            var response = PerformGet(url);

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
        private static string PerformGet(string url)
        {
            var web = (HttpWebRequest)WebRequest.Create(url);
            web.UserAgent = UserAgent;

            var response = web.GetResponse();

            using (var sr = new StreamReader(response.GetResponseStream()))
            {
                return sr.ReadToEnd();
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
        public static double ParseCpuCredit(string response)
        {
            var total = 0.0;
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

                var credit = 0.0;

                if (double.TryParse(response.Substring(beg, len), out credit))
                {
                    total += credit;
                }

                pos = end + GhzDays.Length;
            }

            return total;
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
