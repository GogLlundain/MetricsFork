using CsvHelper.Configuration;

namespace Metrics.Parsers.WebPagetest
{
    public class DetailedWebpagetestRow
    {
        //"Date","Time","Event Name","IP Address","Action","Host","URL",
        //"Response Code","Time to Load (ms) - 8","Time to First Byte (ms)",
        //"Start Time (ms)","Bytes Out","Bytes In","Object Size",
        //"Cookie Size (out)","Cookie Count(out)","Expires","Cache Control",
        //"Content Type","Content Encoding","Transaction Type","Socket ID",
        //"Document ID","End Time (ms)","Descriptor","Lab ID","Dialer ID",
        //"Connection Type","Cached","Event URL","Pagetest Build","Measurement Type",
        //"Experimental","Event GUID","Sequence Number","Cache Score","Static CDN Score",
        //"GZIP Score","Cookie Score","Keep-Alive Score","DOCTYPE Score","Minify Score",
        //"Combine Score","Compression Score","ETag Score","Flagged","Secure","DNS Time",
        //"Connect Time","SSL Time","Gzip Total Bytes","Gzip Savings","Minify Total Bytes",
        //"Minify Savings","Image Total Bytes","Image Savings","Cache Time (sec)",
        //"Real Start Time (ms)","Full Time to Load (ms)","Optimization Checked",
        //"CDN Provider","DNS Start","DNS End","Connect Start","Connect End","SSL Negotiation Start","SSL Negotiation End"
        [CsvField(Index = 0)]
        public string Date { get; set; }

        [CsvField(Index = 1)]
        public string Time { get; set; }

        [CsvField(Ignore = true)]
        public string Timestamp
        {
            get
            {
                return Date + " " + Time;
            }
        }

        [CsvField(Index = 2)]
        public string EventName { get; set; }

        [CsvField(Index = 5)]
        public string Host { get; set; }

        [CsvField(Index = 8)]
        public int TimeToLoad { get; set; }

        [CsvField(Index = 9)]
        public int TimeToFirstByte { get; set; }

        [CsvField(Index = 12)]
        public int Bytes { get; set; }

        [CsvField(Index = 18)]
        public string ContentType { get; set; }

    }
}
