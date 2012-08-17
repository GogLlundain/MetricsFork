using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Parsers.Logging
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
