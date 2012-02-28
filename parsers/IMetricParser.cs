using System;
using System.Collections.Generic;

namespace Metrics.Parsers
{
    public interface IMetricParser
    {
        void GetMetrics(Action<IEnumerable<Metric>> sendMetrics);
    }
}
