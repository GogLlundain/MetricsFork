﻿using System.Security.Cryptography;
using Metrics.Parsers.Logging;
using Metrics.Parsers.LogTail;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Configuration;

[assembly: CLSCompliant(true)]
namespace Metrics.Parsers
{
    public class LogTailParser : IMetricParser
    {
        private readonly LogConfigurationCollection logs;
        private readonly OffsetCursor<long> cursor;
        private GraphiteClient graphiteClient;
        public IMetricsLogger MessageReporter = new NullLogger();

        public LogTailParser()
        {
            var section = ConfigurationManager.GetSection("LogTail") as LogConfigurationSection;
            cursor = new OffsetCursor<long>("log");
            if (section != null)
            {
                logs = section.Logs;
            }
        }

        private IEnumerable<string> GetValidLogfilesFromDirectory(string logFileLocation, string logFilePattern, int maxDaysToProcess)
        {
            try
            {
                var di = new DirectoryInfo(logFileLocation);
                if (di.Exists)
                {
                    var files = di.GetFileSystemInfos(logFilePattern);
                    return (from file in files
                            where file.LastWriteTime.Date > DateTime.Now.Date.AddDays(0 - (maxDaysToProcess + 1))
                            orderby file.LastWriteTime ascending
                            select file.FullName).ToArray();
                }

            }
            catch { }

            return new List<string>();
        }

        private int CalculateTotalFilesToProcess()
        {
            int total = 0;
            foreach (var log in logs)
            {
                foreach (var locationKey in log.Locations.AllKeys)
                {
                    total += GetValidLogfilesFromDirectory(log.Locations[locationKey].Value, log.Pattern, log.MaxDaysToProcess).Count();
                }
            }

            return total;
        }

        public IEnumerable<string> GetMetrics(GraphiteClient client)
        {
            graphiteClient = client;

            MessageReporter.ReportProgress("Calculating total...");
            var totalFilesToProcess = CalculateTotalFilesToProcess();
            MessageReporter.ReportProgress("DONE : Calculating total");

            var count = 0;
            foreach (var log in logs)
            {
                string workerId = log.Name;
                MessageReporter.ReportWorkerStatus(workerId, String.Format("[{0}] Warming up", log.Name));

                //Group files by their date/filename
                var logFiles = new List<Log>();
                foreach (var locationKey in log.Locations.AllKeys)
                {
                    foreach (var file in GetValidLogfilesFromDirectory(log.Locations[locationKey].Value, log.Pattern,
                                                          log.MaxDaysToProcess))
                    {
                        logFiles.Add(new Log { Filename = file, LocationKey = locationKey });
                    }
                }

                //Go through files by their filename (which is the log date)
                foreach (var logFileGroup in logFiles.GroupBy(logFile => Path.GetFileName(logFile.Filename)))
                {
                    var boomerangValues = new ConcurrentBag<Metric>();

                    //Do all files with the same name at the same time
                    Parallel.ForEach(logFileGroup, file =>
                        {
                            MessageReporter.ReportWorkerStatus(workerId,
                                                               String.Format("[{0}] Processing file {1} ",
                                                                             file.LocationKey,
                                                                             file.Filename.Substring(
                                                                                 file.Filename.LastIndexOf("\\", System.StringComparison.Ordinal) + 1)));
                            var readNewLines = ReadTail(file.Filename, log, file.LocationKey, boomerangValues);

                            Interlocked.Increment(ref count);
                            MessageReporter.SetHeadline(String.Format("Processed {0}/{1} ({2:P}) files", count,
                                                                      totalFilesToProcess,
                                                                      (double)count / (double)totalFilesToProcess));

                            MessageReporter.ReportWorkerStatus(workerId,
                                                               String.Format("[{0}] {1} files ", file.LocationKey,
                                                                             readNewLines.HasValue
                                                                                 ? (readNewLines.Value
                                                                                        ? "Finished"
                                                                                        : "Not Changed")
                                                                                 : "Skipped"));
                        });

                    FlushBoomerangMetrics(boomerangValues);
                }


                MessageReporter.ReportWorkerStatus(workerId, String.Format("DONE ALL"));
            }

            return cursor.GetUsedOffsetFiles();
        }

        private bool? ReadTail(string file, LogConfigurationElement log, string locationKey, ConcurrentBag<Metric> boomerangValues)
        {
            var rawValues = new ConcurrentDictionary<LogStatConfigurationElement, ConcurrentBag<Metric>>();

            if (log == null)
            {
                return null;
            }

            //If multiple log parsers use the same file, select the maximum size (including infinite)
            long maxTailMB = log.MaxTailMB;

            //Work out the last read position
            var info = new FileInfo(file);
            long offset;
            string hash = null;

            //Handle rolling single files
            if (log.SingleRollingFile)
            {
                //Read first line to compute hash
                var firstLineStream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                try
                {
                    using (var reader = new StreamReader(firstLineStream))
                    {
                        firstLineStream = null;
                        if (reader.Peek() != -1)
                        {
                            var line = reader.ReadLine();
                            using (var md5 = MD5.Create())
                            {
                                hash = OffsetCursor<long>.GetMD5HashFileName(md5, line);
                            }
                        }
                    }
                }
                catch
                {
                }

                offset = cursor.GetLastRead(log.Name, file, hash);
            }
            else
            {
                offset = cursor.GetLastRead(log.Name, file);
            }

            long lastPosition = offset;

            //If file hasnt changed, don't bother opening
            if (info.Length <= offset)
            {
                return false;
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
            CollateAndSend(log, rawValues, file, lastPosition, hash);

            return true;
        }

        private void CollateAndSend(LogConfigurationElement log, IDictionary<LogStatConfigurationElement,
            ConcurrentBag<Metric>> rawValues, string file, long lastPosition, string hash = null)
        {
            var metrics = new List<Metric>();

            //Aggregate the metrics by their log aggregation method
            foreach (LogStatConfigurationElement stat in log.Stats)
            {
                if (rawValues.ContainsKey(stat))
                {

                    //Aggregate main value
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
                        case "min":
                            metrics.AddRange(from value in rawValues[stat]
                                             group value by new { value.Timestamp, value.Key }
                                                 into metricGroup
                                                 select
                                                     new Metric
                                                     {
                                                         Key = metricGroup.Key.Key,
                                                         Timestamp = metricGroup.Key.Timestamp,
                                                         Value =
                                                             metricGroup.Min(metric => metric.Value)
                                                     });
                            break;
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

                    //Add per second calulcation if required
                    if ((stat.CalculatePerSecond) && (log.TenSecondGroup))
                    {
                        metrics.AddRange(from value in rawValues[stat]
                                         group value by new { value.Timestamp, value.Key }
                                             into metricGroup
                                             select
                                                 new Metric
                                                 {
                                                     Key = metricGroup.Key.Key + "PerSecond",
                                                     Timestamp = metricGroup.Key.Timestamp,
                                                     Value = metricGroup.Count() / 10
                                                 });


                    }
                }
            }

            cursor.StoreLastRead(log.Name, file, lastPosition, hash);
            graphiteClient.SendMetrics(metrics);
        }

        private void FlushBoomerangMetrics(IEnumerable<Metric> boomerangValues)
        {
            var metrics = new List<Metric>();

            //Aggregate the boomerang metrics
            metrics.AddRange(from value in boomerangValues
                             group value by new { value.Timestamp, value.Key }
                                 into metricGroup
                                 select
                                     new Metric
                                     {
                                         Key = metricGroup.Key.Key,
                                         Timestamp = metricGroup.Key.Timestamp,
                                         Value = metricGroup.Key.Key.EndsWith(".count") ? metricGroup.Sum(metric => metric.Value) :
                                             metricGroup.Sum(metric => metric.Value) /
                                             metricGroup.Count()
                                     });

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
                        log.DateFormat, CultureInfo.InvariantCulture, log.AssumeUniversal ? DateTimeStyles.AssumeUniversal : DateTimeStyles.AssumeLocal);
                    //Strip the last second 
                    if (log.TenSecondGroup)
                    {
                        dateTime = dateTime.AddSeconds((dateTime.Second % 10) * -1);
                    }
                }
                catch (FormatException)
                {
                    //If we cant get the datetime then do nothing
                    return;
                }

                //Check if its inside the acceptable range
                if (dateTime < DateTime.Now.AddDays(log.MaxDaysToProcess * -1))
                {
                    return;
                }

                //Do boomerang calculations independetly;
                if (!String.IsNullOrWhiteSpace(log.BoomerangBeacon))
                {
                    //Check if this is a boomerang beacon
                    if (String.Compare(matches[0].Groups["url"].Value, log.BoomerangBeacon, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        GetBoomerangInformation(matches[0].Groups["querystring"].Value, boomerangMetrics, log, dateTime, matches[0].Groups["userAgent"].Value);
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

                    if (!String.IsNullOrWhiteSpace(stat.Match))
                    {
                        found =
                            String.Compare(matches[0].Groups["url"].Value, stat.Match,
                                           StringComparison.OrdinalIgnoreCase) == 0;
                    }
                    else if (!String.IsNullOrWhiteSpace(stat.Prefix))
                    {
                        found = matches[0].Groups["url"].Value.StartsWith(stat.Prefix, StringComparison.OrdinalIgnoreCase);
                    }

                    //If there are extension filters and they werent found, go to the next stat
                    if (((stat.ExtensionsList.Count > 0) || (!String.IsNullOrWhiteSpace(stat.Match)) || (!String.IsNullOrWhiteSpace(stat.Prefix))) && (!found))
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

        private void GetBoomerangInformation(string value, ConcurrentBag<Metric> metrics, LogConfigurationElement log, DateTime timestamp, string userAgent)
        {
            var queryString = HttpUtility.ParseQueryString(value);

            //Try do an ip location lookup
            string locationKey = "{country}.{state}";
            if (!String.IsNullOrEmpty(queryString["user_ip"]))
            {
                if (queryString["user_ip"].StartsWith("10.55."))
                {
                    locationKey = locationKey.Replace("{country}", "local");
                    locationKey = locationKey.Replace("{state}", "local");
                }
                else
                {
                    try
                    {
                        var location = Geolocation.Instance.GetLocation(IPAddress.Parse(queryString["user_ip"]));

                        if (location != null && location.IpLocation != null)
                        {
                            locationKey = locationKey.Replace("{country}", String.IsNullOrWhiteSpace(location.IpLocation.Country) ? "unknown" : location.IpLocation.Country);
                            if (String.Compare(location.IpLocation.Country, "US", StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                locationKey = locationKey.Replace("{state}", String.IsNullOrWhiteSpace(location.IpLocation.State) ? "unknown" : location.IpLocation.State);
                            }
                        }
                    }
                    catch { }
                }
            }

            //If we didnt get a location then mark as unknown
            locationKey = locationKey.Replace("{country}", "unknown");
            locationKey = locationKey.Replace("{state}", "unknown");

            //Get the requested URL
            string url = queryString["u"];
            string urlKey = String.Empty;
            if (!String.IsNullOrEmpty(url))
            {
                //See if it is one of our seperate urls
                foreach (KeyValueConfigurationElement boomerangUrl in log.BoomerangUrls)
                {
                    if (Regex.IsMatch(url, boomerangUrl.Value, RegexOptions.IgnoreCase))
                    {
                        urlKey = boomerangUrl.Key;
                        break;
                    }
                }
            }

            //Try get the browser bersion
            string browserVersion = GetBrowserVersionFromUserAgent(userAgent);

            //Add counter for location
            metrics.Add(new Metric
                {
                    Key = "stats." + log.BoomerangKey + ".location." + locationKey + ".count",
                    Timestamp = timestamp,
                    Value = 1
                });


            //Counter with browser
            metrics.Add(new Metric
                {
                    Key = "stats." + log.BoomerangKey + ".browser." + browserVersion + ".count",
                    Timestamp = timestamp,
                    Value = 1
                });

            //Get start type
            string startKey = "unknown";
            if (!String.IsNullOrWhiteSpace(queryString["rt.start"]))
            {
                startKey = queryString["rt.start"];
            }

            foreach (string queryStringKey in queryString.Keys)
            {
                string fullKey = "timers." + log.BoomerangKey;
                if (String.Compare(queryStringKey, "t_other", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    var otherQueryString =
                        HttpUtility.ParseQueryString(
                            HttpUtility.UrlDecode(queryString[queryStringKey]).Replace("|", "=").Replace(",", "&"));
                    foreach (string otherKey in otherQueryString)
                    {
                        if ((otherKey != null) && (otherKey.StartsWith("t_")))
                        {
                            int otherValue;
                            if (Int32.TryParse(otherQueryString[otherKey], out otherValue))
                            {
                                //Timer by location
                                metrics.Add(new Metric
                                    {
                                        Key =
                                            fullKey + ".location." + locationKey + "." + otherKey.Replace("t_", "") +
                                            ".avg",
                                        Timestamp = timestamp,
                                        Value = otherValue
                                    });
                                //Timer by location & start type
                                metrics.Add(new Metric
                                    {
                                        Key =
                                            fullKey + ".location." + locationKey + "." + startKey + "." +
                                            otherKey.Replace("t_", "") + ".avg",
                                        Timestamp = timestamp,
                                        Value = otherValue
                                    });
                                //Timer by browser
                                metrics.Add(new Metric
                                    {
                                        Key =
                                            fullKey + ".browser." + browserVersion + "." +
                                            otherKey.Replace("t_", "") + ".avg",
                                        Timestamp = timestamp,
                                        Value = otherValue
                                    });
                                //Timer by browser & start type
                                metrics.Add(new Metric
                                    {
                                        Key =
                                            fullKey + ".browser." + browserVersion + "." + startKey + "." +
                                            otherKey.Replace("t_", "") + ".avg",
                                        Timestamp = timestamp,
                                        Value = otherValue
                                    });
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
                        //Timer by location
                        metrics.Add(new Metric
                            {
                                Key =
                                    fullKey + ".location." + locationKey + "." + queryStringKey.Replace("t_", "") +
                                    ".avg",
                                Timestamp = timestamp,
                                Value = otherValue
                            });
                        //Timer by location & start type
                        metrics.Add(new Metric
                            {
                                Key =
                                    fullKey + ".location." + locationKey + "." + startKey + "." +
                                    queryStringKey.Replace("t_", "") + ".avg",
                                Timestamp = timestamp,
                                Value = otherValue
                            });
                        //Timer by browser
                        metrics.Add(new Metric
                            {
                                Key =
                                    fullKey + ".browser." + browserVersion + "." + queryStringKey.Replace("t_", "") +
                                    ".avg",
                                Timestamp = timestamp,
                                Value = otherValue
                            });
                        //Timer by browser & start type
                        metrics.Add(new Metric
                            {
                                Key =
                                    fullKey + ".browser." + browserVersion + "." + startKey + "." +
                                    queryStringKey.Replace("t_", "") + ".avg",
                                Timestamp = timestamp,
                                Value = otherValue
                            });

                        //------------
                        //TODO : Tidy up this huge mess
                        //------------
                        if ((!String.IsNullOrEmpty(urlKey))
                            &&
                            ((queryStringKey.Replace("t_", "") == "done") ||
                             (queryStringKey.Replace("t_", "") == "resp")))
                        {
                            //Timer by location & start type & page
                            metrics.Add(new Metric
                                {
                                    Key =
                                        fullKey + ".pages." + urlKey + ".location." + locationKey + "." + startKey +
                                        "." + queryStringKey.Replace("t_", "") + ".avg",
                                    Timestamp = timestamp,
                                    Value = otherValue
                                });
                            //Timer by browser & start type
                            metrics.Add(new Metric
                                {
                                    Key =
                                        fullKey + ".pages." + urlKey + ".browser." + browserVersion + "." + startKey +
                                        "." + queryStringKey.Replace("t_", "") + ".avg",
                                    Timestamp = timestamp,
                                    Value = otherValue
                                });
                        }
                    }
                }
            }
        }

        private static string GetBrowserVersionFromUserAgent(string userAgent)
        {
            string browserVersion = "unknown.unknown";

            try
            {
                var browser = new HttpBrowserCapabilities
                {
                    Capabilities = new Hashtable { { string.Empty, userAgent } }
                };
                var factory = new BrowserCapabilitiesFactory();
                factory.ConfigureBrowserCapabilities(new NameValueCollection(), browser);

                if (String.Compare(browser.Browser, "mozilla", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (userAgent.ToLowerInvariant().Contains("msie+10.0"))
                    {
                        browserVersion = "msie.10";
                    }
                    else if (userAgent.ToLowerInvariant().Contains("msie+9.0"))
                    {
                        browserVersion = "msie.9";
                    }
                    else if (userAgent.ToLowerInvariant().Contains("msie+8.0"))
                    {
                        browserVersion = "msie.8";
                    }
                    else if (userAgent.ToLowerInvariant().Contains("msie+7.0"))
                    {
                        browserVersion = "msie.7";
                    }
                    else if (userAgent.ToLowerInvariant().Contains("msie+6.0"))
                    {
                        browserVersion = "msie.6";
                    }
                    else if (userAgent.ToLowerInvariant().Contains("msie"))
                    {
                        browserVersion = "msie.unknown";
                    }
                }
                else
                {
                    browserVersion = browser.Browser.ToLowerInvariant() + "." + browser.MajorVersion;
                }
            }
            catch { }

            return browserVersion;
        }
    }
}
