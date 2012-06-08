using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Metrics.Parsers.LogTail;

[assembly: CLSCompliant(true)]
namespace Metrics.Parsers
{
    public class LogTailParser : IMetricParser
    {
        private readonly LogConfigurationCollection logs;
        private readonly OffsetCursor<long> cursor;
        private GraphiteClient graphiteClient;

        public LogTailParser()
        {
            var section = ConfigurationManager.GetSection("LogTail") as LogConfigurationSection;
            cursor = new OffsetCursor<long>("log");
            if (section != null)
            {
                logs = section.Logs;
            }
        }

        public IEnumerable<string> GetMetrics(GraphiteClient client)
        {
            graphiteClient = client;

            Parallel.ForEach(logs, log =>
                                       {
                                           foreach (var locationKey in log.Locations.AllKeys)
                                           {
                                               foreach (var file in Directory.GetFiles(log.Locations[locationKey].Value, log.Pattern))
                                               {
                                                   ReadTail(file, log, locationKey);
                                               }
                                           }
                                       });

            return cursor.GetUsedOffsetFiles();
        }

        private void ReadTail(string file, LogConfigurationElement log, string locationKey)
        {
            var rawValues = new ConcurrentDictionary<LogStatConfigurationElement, ConcurrentBag<Metric>>();

            if (log == null)
            {
                return;
            }

            //If multiple log parsers use the same file, select the maximum size (including infinite)
            long maxTailMB = log.MaxTailMB;

            //Work out the last read position
            var info = new FileInfo(file);

            //Skip if only today is set and file wasn't updated today
            if ((log.OnlyToday) && (info.LastWriteTime.Date < DateTime.Now.Date))
            {
                return;
            }

            var offset = cursor.GetLastRead(file);
            long lastPosition = offset;

            //If file hasnt changed, don't bother opening
            if (info.Length <= offset)
            {
                return;
            }

            //If the file is greater than our maxTailMB setting, skip to the maximum and proceed
            if (maxTailMB > 0)
            {
                long maxTailBytes = maxTailMB * 1048576;
                if (info.Length - offset > maxTailBytes)
                {
                    offset = info.Length - maxTailBytes;
                }
            }

            //Loop through the file doing matches
            var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            try
            {
                using (var reader = new StreamReader(stream))
                {
                    stream = null;
                    reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                    long lineCount = 0;

                    while (reader.Peek() != -1)
                    {
                        var line = reader.ReadLine();
                        lineCount++;

                        //Update the lastPosition counter before we try parse so any failures do not repeat the same line
                        lastPosition = reader.BaseStream.Position;
                        ParseLine(log, line, rawValues, locationKey);

                        //Flush every 1000 lines to minimise memory footprint
                        if (lineCount >= 1000)
                        {
                            //send a debug metric
                            graphiteClient.SendQuickMetric("metrics.logLines.count", (int)lineCount);

                            //send real metrics to graphite
                            CollateAndSend(log, rawValues, file, lastPosition);
                            rawValues.Clear();
                            lineCount = 0;
                        }
                    }

                    //send a debug metric
                    graphiteClient.SendQuickMetric("metrics.logLines.count", (int)lineCount);
                }
            }
            finally
            {
                if (stream != null)
                {
                    stream.Dispose();
                }
            }

            graphiteClient.SendQuickMetric("metrics.logLines.count", rawValues.Count);
            CollateAndSend(log, rawValues, file, lastPosition);
        }

        private void CollateAndSend(LogConfigurationElement log, IDictionary<LogStatConfigurationElement, ConcurrentBag<Metric>> rawValues, string file, long lastPosition)
        {
            var metrics = new List<Metric>();

            //Aggregate the metrics by their log aggregation method
            foreach (LogStatConfigurationElement stat in log.Stats)
            {
                if (rawValues.ContainsKey(stat))
                {
                    switch (stat.AggregateType)
                    {
                        case "count":
                            metrics.AddRange(from value in rawValues[stat]
                                             group value by new { value.Timestamp, value.Key }
                                                 into metricGroup
                                                 select
                                                     new Metric
                                                     {
                                                         Key = metricGroup.Key.Key,
                                                         Timestamp = metricGroup.Key.Timestamp,
                                                         Value = metricGroup.Count()
                                                     });
                            break;
                        case "max":
                            metrics.AddRange(from value in rawValues[stat]
                                             group value by new { value.Timestamp, value.Key }
                                                 into metricGroup
                                                 select
                                                     new Metric
                                                     {
                                                         Key = metricGroup.Key.Key,
                                                         Timestamp = metricGroup.Key.Timestamp,
                                                         Value =
                                                             metricGroup.Max(metric => metric.Value)
                                                     });
                            break;
                        case "avg":
                        default:
                            metrics.AddRange(from value in rawValues[stat]
                                             group value by new { value.Timestamp, value.Key }
                                                 into metricGroup
                                                 select
                                                     new Metric
                                                     {
                                                         Key = metricGroup.Key.Key,
                                                         Timestamp = metricGroup.Key.Timestamp,
                                                         Value =
                                                             metricGroup.Sum(metric => metric.Value) /
                                                             metricGroup.Count()
                                                     });
                            break;
                    }
                }
            }

            cursor.StoreLastRead(file, lastPosition);
            graphiteClient.SendMetrics(metrics);
        }

        private void ParseLine(LogConfigurationElement log, string line, ConcurrentDictionary<LogStatConfigurationElement, ConcurrentBag<Metric>> rawValues, string locationKey)
        {
            var matches = log.CompiledRegex.Matches(line);

            //Loop through all the various stats
            foreach (LogStatConfigurationElement stat in log.Stats)
            {
                if (matches.Count > 0)
                {
                    if ((stat.ExtensionsList.Count > 0) && (matches[0].Groups["url"] == null))
                    {
                        throw new InvalidOperationException("Stat extensions is not null, but no \"url\" is specified in the regex");
                    }
                    
                    var key = stat.GraphiteKey.Replace("{locationKey}", locationKey);

                    //Check if there is an extensions filter
                    var found = false;
                    foreach (var extension in stat.ExtensionsList)
                    {
                        if (matches[0].Groups["url"].Value.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            key = key.Replace("{extension}", extension.TrimStart('.'));
                        }
                    }

                    //If there are extension filters and they werent found, go to the next stat
                    if ((stat.ExtensionsList.Count > 0) && (!found))
                    {
                        continue;
                    }

                    //do key replacements
                    foreach (var map in log.Mapping.AllKeys)
                    {
                        if (log.Mapping[map].Value.StartsWith("?", StringComparison.Ordinal))
                        {
                            key = key.Replace("{" + map + "}",
                                              matches[0].Groups[log.Mapping[map].Value.TrimStart('?')].Value);
                        }
                        else
                        {
                            key = key.Replace("{" + map + "}", log.Mapping[map].Value);
                        }
                    }

                    try
                    {
                        var dateTime = DateTime.ParseExact(matches[0].Groups[stat.Interval].Value,
                                                           stat.DateFormat, CultureInfo.InvariantCulture);

                        //Create metric
                        var metric = new Metric
                                         {
                                             Key = key,
                                             Timestamp = dateTime
                                         };
                        if (!String.IsNullOrEmpty(stat.Value))
                        {
                            metric.Value = Int32.Parse(matches[0].Groups[stat.Value].Value, CultureInfo.InvariantCulture);
                        }


                        if (stat.IncludeZeros || (!stat.IncludeZeros && metric.Value > 0))
                        {
                            rawValues.TryAdd(stat, new ConcurrentBag<Metric>());
                            rawValues[stat].Add(metric);
                        }
                    }
                    catch
                    {
                        graphiteClient.SendQuickMetric("metrics.runTime.avg", 1);
                    }
                }
            }
        }
    }
}
