using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Metrics.Parsers.LogTail;
using System.Linq;

namespace Metrics.Parsers
{
    public class LogTailParser : IMetricParser
    {
        private readonly LogConfigurationCollection logs;
        private readonly ConcurrentDictionary<string, long> lastByteRead = new ConcurrentDictionary<string, long>();

        public LogTailParser()
        {
            var section = ConfigurationManager.GetSection("LogTail") as LogConfigurationSection;
            if (section != null)
            {
                logs = section.Logs;
            }
        }

        public IDictionary<string, Metric> GetMetrics()
        {
            var metrics = new ConcurrentDictionary<string, Metric>();
            Parallel.ForEach(logs.Cast<LogConfigurationElement>(), (log) => ReadTail(log, metrics));

            return metrics;
        }

        private void ReadTail(LogConfigurationElement log, ConcurrentDictionary<string, Metric> metrics)
        {
            foreach (var file in Directory.GetFiles(log.Location, log.Pattern))
            {
                var offset = lastByteRead.ContainsKey(file) ? lastByteRead[file] : 0;

                using (var stream = File.OpenRead(file))
                {
                    using (var reader = new StreamReader(stream))
                    {
                        reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                        while (reader.Peek() != -1)
                        {
                            var line = reader.ReadLine();
                            if (!lastByteRead.TryAdd(file, reader.BaseStream.Position))
                            {
                                lastByteRead.TryUpdate(file, reader.BaseStream.Position, offset);
                            }
                            ParseLine(log, line);
                        }
                    }
                }
            }
        }

        private static void ParseLine(LogConfigurationElement log, string line)
        {
            var matches = log.CompiledRegex.Matches(line);
            if (matches.Count > 0)
            {
                string key = log.GraphiteKey;
                //do key replacements
                foreach (var map in log.Mapping.AllKeys)
                {
                    if (log.Mapping[map].Value.StartsWith("?"))
                    {
                        key = key.Replace("{" + map + "}", matches[0].Groups[log.Mapping[map].Value.TrimStart('?')].Value);
                    }
                    else
                    {
                        key = key.Replace("{" + map + "}", log.Mapping[map].Value);
                    }
                }

                var timestamp = DateTime.ParseExact(matches[0].Groups[log.Interval].Value, log.DateFormat,
                             CultureInfo.InvariantCulture);
                int value = 0;
                switch (log.AggregateType)
                {
                    case "avg":
                        value = Int32.Parse()
                        break;
                    case "countLine":
                        break;
                }
            }

        }
    }
}
