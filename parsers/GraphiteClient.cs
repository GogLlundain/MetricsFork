using System;
using System.Collections.Generic;
using System.Configuration;
using Graphite;

namespace Metrics.Parsers
{
    public class GraphiteClient
    {
        private readonly string host;
        private readonly int port;
        private readonly string prefix;

        public GraphiteClient(string host, int port, string prefix = "")
        {
            this.host = host;
            this.port = port;
            this.prefix = prefix;
        }

        /// <summary>
        /// Sends the gathered stats to Graphite
        /// </summary>
        public void SendMetrics(IEnumerable<Metric> metrics)
        {
            using (var graphiteClient = new GraphiteTcpClient(host, port, prefix))
            {
                foreach (var metric in metrics)
                {
                    graphiteClient.Send(metric.Key, metric.Value, metric.Timestamp);
                }
            }
        }

        /// <summary>
        /// Sends a quick metric to graphite
        /// </summary>
        public void SendQuickMetric(string key, int value, DateTime timestamp)
        {
            using (var graphiteClient = new GraphiteTcpClient(host, port, prefix))
            {
                graphiteClient.Send(key, value, timestamp);
            }
        }

        /// <summary>
        /// Sends a quick metric to graphite
        /// </summary>
        public void SendQuickMetric(string key, int value)
        {
            SendQuickMetric(key, value, DateTime.Now);
        }
    }
}
