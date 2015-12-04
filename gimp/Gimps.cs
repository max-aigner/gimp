namespace gimp
{
    using System;
    using System.Text;
    using System.Net;
    using System.IO;
    using System.Diagnostics;
    using System.Collections.Generic;

    public class Gimps
    {
        private const string WebLogsDir = "weblogs\\";
        private const string TestKey = "Test";
        private const string DblChkKey = "DoubleCheck";

        const string BeginBlockMarker = "<!--BEGIN_ASSIGNMENTS_BLOCK-->";
        const string EndBlockMarker = "<!--END_ASSIGNMENTS_BLOCK-->";
        const string UserValidationPhrase = "PROCESSING_VALIDATION:ASSIGNED TO ";
        const string CpuCredit = "CPU credit is ";
        const string GhzDays = " GHz-days.";

        private const string Accept = "text/html, application/xhtml+xml, */*";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 6.3; WOW64; Trident/7.0; rv:11.0) like Gecko";
        private const string UserIdMarker = "name=\"was_logged_in_as\" value=\"";

        public enum PreferredWorkType
        {
            WorldRecordTests = 102,
            SmallestAvailableFirstTimeTests = 100,
            DoubleCheckTests = 101,
            P1Factoring = 4,
            TrialFactoring = 2,
            ECMFactoring = 5,
            HundredMillionDigitTest = 104
        }

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

        private static string PerformGet(string url)
        {
            HttpWebRequest web = (HttpWebRequest)WebRequest.Create(url);
            web.UserAgent = UserAgent;

            WebResponse response = web.GetResponse();

            using (StreamReader sr = new StreamReader(response.GetResponseStream()))
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
            var boundary = string.Format("---------------------------{0:x}", DateTime.Now.Ticks).Substring(0, 40);
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

            return assignments.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

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

        private static void LogResponse(
            string logId,
            string method,
            string response)
        {
            File.WriteAllText(
                WebLogsDir + logId + "." + method + ".html",
                response);
        }
    }
}
