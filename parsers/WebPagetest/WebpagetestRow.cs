using CsvHelper.Configuration;

namespace Metrics.Parsers.WebPagetest
{
    public class WebpagetestRow
    {
        [CsvField(Name = "Date/Time")]
        public string Timestamp { get; set; }

        [CsvField(Name = "Location")]
        public string Location { get; set; }

        [CsvField(Name = "Test ID")]
        public string TestId { get; set; }

        [CsvField(Name = "URL")]
        public string Url { get; set; }
    }
}
