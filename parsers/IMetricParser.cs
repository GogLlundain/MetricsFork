using System.Collections.Generic;

namespace Metrics.Parsers
{
    public interface IMetricParser
    {
        IEnumerable<Metric> GetMetrics();
    }
}
