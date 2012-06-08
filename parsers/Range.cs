namespace Metrics.Parsers
{
    public class Range
    {
        public long Start { get; set; }
        public long End { get; set; }
        public Location IpLocation { get; set; }

        public override string ToString()
        {
            return IpLocation.ToString();
        }
    }
}
