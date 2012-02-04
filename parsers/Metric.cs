using System;

namespace Metrics.Parsers
{
    public class Metric
    {
        public string Key { get; set; }
        public DateTime Timestamp { get; set; }
        public int Value { get; set; }
    }
}
