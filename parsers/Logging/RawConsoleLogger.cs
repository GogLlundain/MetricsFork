using System;
using System.Collections.Concurrent;

namespace Metrics.Parsers.Logging
{
    public class RawConsoleLogger : IMetricsLogger
    {
        public RawConsoleLogger()
        {
            Console.Clear();
        }

        public void ReportProgress(string message)
        {
            Console.WriteLine(DateTime.Now.ToLongTimeString() + " :: " + message);
        }

        public void ReportWorkerStatus(string id, string message)
        {
            ReportProgress(message);
        }

        public void SetHeadline(string message)
        {
        }
    }
}
