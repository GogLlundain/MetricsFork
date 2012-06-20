using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
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
            var boomerangValues = new ConcurrentBag<Metric>();

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
                        ParseLine(log, line, rawValues, boomerangValues, locationKey);

                        //Flush every 1000 lines to minimise memory footprint
                        if (lineCount >= 1000)
                        {
                            //send a debug metric
                            graphiteClient.SendQuickMetric("metrics.logLines.count", (int)lineCount);

                            //send real metrics to graphite
                            CollateAndSend(log, rawValues, file, lastPosition);
                            rawValues.Clear();
                            lineCount = 0;

                            //Send boomerang metrics
                            graphiteClient.SendMetrics(boomerangValues);
                            boomerangValues = new ConcurrentBag<Metric>();
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

            //Send boomerang metrics
            graphiteClient.SendMetrics(boomerangValues);

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

        private void ParseLine(LogConfigurationElement log, string line, ConcurrentDictionary<LogStatConfigurationElement, ConcurrentBag<Metric>> rawValues, ConcurrentBag<Metric> boomerangMetrics, string locationKey)
        {
            var matches = log.CompiledRegex.Matches(line);

            if (matches.Count > 0)
            {
                DateTime dateTime;
                try
                {
                    dateTime = DateTime.ParseExact(matches[0].Groups[log.Interval].Value,
                                                               log.DateFormat, CultureInfo.InvariantCulture);
                } catch (FormatException)
                {
                    //If we cant get the datetime then do nothing
                    return;
                }

                //Do boomerang calculations independetly;
                if (!String.IsNullOrWhiteSpace(log.BoomerangBeacon))
                {
                    if (matches[0].Groups["url"].Value == log.BoomerangBeacon)
                    {
                        GetBoomerangInformation(matches[0].Groups["querystring"].Value, boomerangMetrics, log.BoomerangKey, dateTime);
                    }
                }

                //Loop through all the various stats
                foreach (LogStatConfigurationElement stat in log.Stats)
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

        private void GetBoomerangInformation(string value, ConcurrentBag<Metric> metrics, string key, DateTime timestamp)
        {
            var queryString = HttpUtility.ParseQueryString(value);

            //Try do an ip location lookup
            if (!String.IsNullOrEmpty(queryString["user_ip"]))
            {
                if (queryString["user_ip"].StartsWith("10.55."))
                {
                    key = key.Replace("{country}", "local");
                    key = key.Replace("{state}", "local");
                }
                else
                {
                    try
                    {
                        var location = Geolocation.Instance.GetLocation(IPAddress.Parse(queryString["user_ip"]));

                        if (location != null && location.IpLocation != null)
                        {
                            key = key.Replace("{country}", String.IsNullOrWhiteSpace(location.IpLocation.Country) ? "unknown" : location.IpLocation.Country);
                            if (String.Compare(location.IpLocation.Country, "US", StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                key = key.Replace("{state}", String.IsNullOrWhiteSpace(location.IpLocation.State) ? "unknown" : location.IpLocation.State);
                            }
                        }
                    }
                    catch { }
                }
            }

            //If we didnt get a location then mark as unknown
            key = key.Replace("{country}", "unknown");
            key = key.Replace("{state}", "unknown");

            //Add counter (before we decide where it came from to make aggregation easier
            metrics.Add(new Metric { Key = "stats." + key + ".count", Timestamp = timestamp, Value = 1 });

            //Get start type
            if (!String.IsNullOrWhiteSpace(queryString["rt.start"]))
            {
                key = key + "." + queryString["rt.start"];
            }
            else
            {
                key = key + ".unknown";
            }

            foreach (string queryStringKey in queryString.Keys)
            {
                string fullKey = "timers." + key;
                if (String.Compare(queryStringKey, "t_other", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    var otherQueryString = HttpUtility.ParseQueryString(HttpUtility.UrlDecode(queryString[queryStringKey]).Replace("|", "=").Replace(",", "&"));
                    foreach (string otherKey in otherQueryString)
                    {
                        if ((otherKey != null) && (otherKey.StartsWith("t_")))
                        {
                            int otherValue;
                            if (Int32.TryParse(otherQueryString[otherKey], out otherValue))
                            {
                                metrics.Add(new Metric { Key = fullKey + "." + otherKey.Replace("t_", "") + ".avg", Timestamp = timestamp, Value = otherValue });
                                //metrics.Add(new Metric { Key = fullKey + "." + otherKey.Replace("t_", "") + ".max", Timestamp = timestamp, Value = otherValue });
                            }
                        }
                    }

                    continue;
                }

                if ((queryStringKey != null) && (queryStringKey.StartsWith("t_")))
                {
                    int otherValue;
                    if (Int32.TryParse(queryString[queryStringKey], out otherValue))
                    {
                        metrics.Add(new Metric { Key = fullKey + "." + queryStringKey.Replace("t_", "") + ".avg", Timestamp = timestamp, Value = otherValue });
                        //metrics.Add(new Metric { Key = fullKey + "." + queryStringKey.Replace("t_", "") + ".max", Timestamp = timestamp, Value = otherValue });
                    }
                }

            }
        }
    }
}
