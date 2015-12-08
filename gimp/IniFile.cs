namespace Gimp
{
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.IO;

    public class IniFile : NameValueCollection
    {
        private char separator = ':';
        private string path;

        /// <summary>
        /// Initializes a new instance of the <see cref="IniFile"/> class.
        /// </summary>
        /// <param name="path">Ini file name.</param>
        public IniFile(string path)
        {
            Path = path;

            Read();
        }

        /// <summary>
        /// Gets the path of the ini file.
        /// </summary>
        public string Path
        {
            get
            {
                return path;
            }

            private set
            {
                path = value;
            }
        }

        /// <summary>
        /// Writes current version of ini file to disk.
        /// </summary>
        public void Refresh()
        {
            var lines = new List<string>();

            foreach (var key in AllKeys)
            {
                foreach (var value in GetValues(key))
                {
                    var line = string.Format("{0}{1}{2}", key, separator, value);

                    lines.Add(line);
                }
            }

            File.WriteAllLines(Path, lines);
        }

        private void Read()
        {
            var lines = File.ReadAllLines(Path);

            foreach (var line in lines)
            {
                var trim = line.Trim();

                if (trim.StartsWith("#"))
                {
                    continue;
                }

                var eq = trim.IndexOf(separator);

                if (eq < 0)
                {
                    continue;
                }

                var name = trim.Substring(0, eq).Trim();
                var value = trim.Substring(eq + 1).Trim();

                base.Add(name, value);
            }
        }
    }
}
