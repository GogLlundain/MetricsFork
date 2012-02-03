using System.Collections.Generic;

namespace Metrics.Parsers
{
    public interface IMetricParser
    {
        IDictionary<string, Metric> GetMetrics();
    }
}
