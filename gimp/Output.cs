namespace Gimp
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Handles printing progress messages to the standard output channel.
    /// </summary>
    public class Output
    {
        /// <summary>
        /// The currently selected facility.
        /// </summary>
        private Facility facility = Facility.None;

        public enum Facility
        {
            /// <summary>
            /// Invalid facility.
            /// </summary>
            None = 0,

            /// <summary>
            /// Start facility.
            /// </summary>
            Start = 1,

            /// <summary>
            /// Initialization facility.
            /// </summary>
            Init = 2,

            /// <summary>
            /// Statistics facility.
            /// </summary>
            Stats = 3,

            /// <summary>
            /// Assignment facility.
            /// </summary>
            Assign = 4,

            /// <summary>
            /// Result facility.
            /// </summary>
            Result = 5,

            /// <summary>
            /// Upload facility.
            /// </summary>
            Upload = 6
        }

        public void PrintStatus(Facility facility, string status)
        {
            if (this.facility != facility)
            {
                this.facility = facility;

                Console.WriteLine();
            }

            if (string.IsNullOrWhiteSpace(status))
            {
                Console.WriteLine(
                    "{0} {1}",
                    DateTime.Now.ToString("MMM dd HH:mm:ss"),
                    facility);
            }
            else
            {
                Console.WriteLine(
                    "{0} {1}: {2}",
                    DateTime.Now.ToString("MMM dd HH:mm:ss"),
                    facility,
                    status);
            }
        }
    }
}
