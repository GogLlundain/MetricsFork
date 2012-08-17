using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Parsers.Logging
{
    public interface IMetricsLogger
    {
        void ReportProgress(string message);
        void ReportWorkerStatus(string id, string message);

        void SetHeadline(string message);
    }
}
