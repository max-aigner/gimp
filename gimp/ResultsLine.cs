namespace Gimp
{
    using System;

    public class ResultsLine
    {
        public string CpuName { get; set; }
        public long Exponent { get; set; }
        public string ResultType { get; set; }
        public DateTime Received { get; set; }
        public TimeSpan Age { get; set; }
        public string Result { get; set; }
        public decimal Credit { get; set; }

        public override string ToString()
        {
            return string.Format(
                "{0}~{1}~{2}~{3}~{4}~{5}~{6}",
                CpuName,
                Exponent,
                ResultType,
                Received,
                Age,
                Result,
                Credit);
        }
    }
}
