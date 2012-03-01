using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using Metrics.Parsers;

namespace Metrics
{
    class Program
    {
        static void Main()
        {
            var client = new GraphiteClient(ConfigurationManager.AppSettings["GraphiteHost"],
                                            Int32.Parse(ConfigurationManager.AppSettings["GraphitePort"]),
                                            ConfigurationManager.AppSettings["GraphiteKeyPrefix"]);

            var filesUsed = new List<string>();

            //Debug information
            var sw = new Stopwatch();
            sw.Start();

            //Configuration based.  Config file lists logs, sizes, regex, etc
            var logParser = new LogTailParser();
            filesUsed.AddRange(logParser.GetMetrics(client));

            //WPT parsers
            var wptParser = new WebPagetestParser();
            filesUsed.AddRange(wptParser.GetMetrics(client));

            sw.Stop();
            client.SendQuickMetric("metrics.runTime.avg", (int)sw.ElapsedMilliseconds);

            //Cleanup old offset files
            foreach (var file in Directory.GetFiles(".", "*.offset"))
            {
                if (!filesUsed.Contains(Path.GetFileName(file)))
                {
                    File.Delete(file);
                }
            }
        }


    }
}
