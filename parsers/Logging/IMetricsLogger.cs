namespace Metrics.Parsers.Logging
{
    public interface IMetricsLogger
    {
        void ReportProgress(string message);
        void ReportWorkerStatus(string id, string message);

        void SetHeadline(string message);
    }
}
