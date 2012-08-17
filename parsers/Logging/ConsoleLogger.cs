using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Parsers.Logging
{
    public class ConsoleLogger : IMetricsLogger
    {
        private string headlineMessage = String.Empty;
        private DateTime lastUpdateTime = DateTime.Now;
        private int millisecondsBetweenUpdates = 500;
        private ConcurrentDictionary<string, string> workerStatuses = new ConcurrentDictionary<string, string>();
        private object prisoner = new object();

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
            foreach (string workerId in workerStatuses.Keys)
            {
                Console.WriteLine(String.Format("{0} - {1}", workerId, workerStatuses[workerId]));
            }
        }

        public void SetHeadline(string message)
        {
            headlineMessage = message;
            RenderResultsTable();
        }

        private void RenderResultsTable()
        {
            if (DateTime.Now > lastUpdateTime.AddMilliseconds(millisecondsBetweenUpdates))
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
