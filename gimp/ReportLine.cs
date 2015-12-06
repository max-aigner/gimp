namespace Gimp
{
    public class ReportLine
    {
        public int Rank { get; set; }
        public string Member { get; set; }
        public double Credit { get; set; }
        public int Attempts { get; set; }
        public int Successes { get; set; }

        public override string ToString()
        {
            return string.Format(
                "{0}~{1}~{2}~{3}~{4}",
                Rank,
                Member,
                Credit,
                Attempts,
                Successes);
        }
    }
}
