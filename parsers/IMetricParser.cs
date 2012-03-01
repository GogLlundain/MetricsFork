using System.Collections.Generic;

namespace Metrics.Parsers
{
    public interface IMetricParser
    {
        IEnumerable<string> GetMetrics(GraphiteClient client);
    }
}
