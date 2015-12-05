namespace Gimp
{
    using System;

    class Worker
    {
        public string Directory { get; set; }

        public string WorkTodoFileName { get; set; }
        public DateTime WorkTodoTimestamp { get; set; }

        public string ResultsFileName { get; set; }
        public DateTime ResultsTimestamp { get; set; }
    }
}
