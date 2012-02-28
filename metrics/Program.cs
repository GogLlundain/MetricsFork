using System;
using System.Collections.Generic;
using System.Configuration;
using Metrics.Parsers;
using Graphite;

namespace Metrics
{
    class Program
    {
        static void Main()
        {

            //Configuration based.  Config file lists logs, sizes, regex, etc
            var logParser = new LogTailParser();
            logParser.GetMetrics(SendMetricsToGraphite);

            //WPT parsers
            var wptParser = new WebPagetestParser();
            wptParser.GetMetrics(SendMetricsToGraphite);
        }

        /// <summary>
        /// Sends the gathered stats to Graphite
        /// </summary>
        private static void SendMetricsToGraphite(IEnumerable<Metric> metrics)
        {
            using (var graphiteClient = new GraphiteTcpClient(ConfigurationManager.AppSettings["GraphiteHost"],
                Int32.Parse(ConfigurationManager.AppSettings["GraphitePort"]), ConfigurationManager.AppSettings["GraphiteKeyPrefix"]))
            {
                foreach (var metric in metrics)
                {
                    graphiteClient.Send(metric.Key, metric.Value, metric.Timestamp);
                }
            }
        }
    }
}
