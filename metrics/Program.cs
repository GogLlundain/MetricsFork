using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Metrics.Parsers.WebPagetest;
using System.Configuration;
using System.Xml.XPath;
using Metrics.Parsers;
using Graphite;

namespace Metrics
{
    class Program
    {
        static void Main(string[] args)
        {
            //Configuration based.  Config file lists logs, sizes, regex, etc
            LogTailParser parser = new LogTailParser();
            SendMetricsToGraphite(parser.GetMetrics());
        }

        /// <summary>
        /// Sends the gathered stats to Graphite
        /// TODO : Switch to statsd if frequent interval to stop spamming
        /// TODO : - Graphite used for historical adds to support intervals > finest whisper resolution
        /// </summary>
        private static void SendMetricsToGraphite(IEnumerable<Metric> metrics)
        {
            using (var graphiteClient = new GraphiteTcpClient(ConfigurationManager.AppSettings["GraphiteHost"], Int32.Parse(ConfigurationManager.AppSettings["GraphitePort"]), ConfigurationManager.AppSettings["GraphiteKeyPrefix"]))
            {
                foreach (var metric in metrics)
                {
                    graphiteClient.Send(metric.Key, metric.Value, metric.Timestamp);
                }
            }
        }
    }
}
