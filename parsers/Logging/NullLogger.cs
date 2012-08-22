namespace Metrics.Parsers.Logging
{
    public class NullLogger : IMetricsLogger
    {
        public void ReportProgress(string message)
        {
        }

        public void ReportWorkerStatus(string id, string message)
        {
        }

        public void SetHeadline(string message)
        {
        }
    }
}
