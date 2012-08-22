using System;
using System.Collections.Concurrent;

namespace Metrics.Parsers.Logging
{
    public class ConsoleLogger : IMetricsLogger
    {
        private string headlineMessage = String.Empty;
        private DateTime lastUpdateTime = DateTime.Now;
        private const int MillisecondsBetweenUpdates = 500;
        private readonly ConcurrentDictionary<string, string> workerStatuses = new ConcurrentDictionary<string, string>();
        private readonly object prisoner = new object();

        public ConsoleLogger()
        {
            Console.Clear();
        }

        public void ReportProgress(string message)
        {
            Console.WriteLine(DateTime.Now.ToLongTimeString() + " :: " + message);
        }

        public void ReportWorkerStatus(string id, string message)
        {
            lock (prisoner)
            {
                if (workerStatuses.ContainsKey(id))
                {
                    workerStatuses[id] = message;
                }
                else
                {
                    workerStatuses.TryAdd(id, message);
                }
            }

            RenderResultsTable();
        }

        private void UpdateProgressTable()
        {
            Console.SetCursorPosition(0, 3);
            foreach (var workerId in workerStatuses.Keys)
            {
                Console.WriteLine("{0} - {1}", workerId, workerStatuses[workerId]);
            }
        }

        public void SetHeadline(string message)
        {
            headlineMessage = message;
            RenderResultsTable();
        }

        private void RenderResultsTable()
        {
            if (DateTime.Now > lastUpdateTime.AddMilliseconds(MillisecondsBetweenUpdates))
            {
                Console.Clear();
                Console.SetCursorPosition(0, 0);
                Console.WriteLine(headlineMessage);
                UpdateProgressTable();
                lastUpdateTime = DateTime.Now;
            }
        }
    }
}
