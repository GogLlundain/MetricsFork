using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Metrics.Parsers.LogTail;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

[assembly: CLSCompliant(true)]
namespace Metrics.Parsers
{
    public class LogTailParser : IMetricParser
    {
        private readonly LogConfigurationCollection logs;

        public LogTailParser()
        {
            var section = ConfigurationManager.GetSection("LogTail") as LogConfigurationSection;
            if (section != null)
            {
                logs = section.Logs;
            }
        }

        private static long GetLastByteRead(string filePath)
        {
            try
            {
                using (var md5 = MD5.Create())
                {
                    string tempName = GetMD5HashFileName(md5, filePath);
                    if (!File.Exists(tempName))
                    {
                        return 0;
                    }

                    long offset;
                    var lines = File.ReadAllLines(tempName);
                    if (Int64.TryParse(lines[0], out offset))
                    {
                        return offset;
                    }
                    return 0;
                }
            }
            catch
            {
                return 0;
            }
        }

        private static void StoreLastByteRead(string filePath, long offset)
        {
            using (var md5 = MD5.Create())
            {
                var tempName = GetMD5HashFileName(md5, filePath);
                File.WriteAllText(tempName, offset.ToString(CultureInfo.InvariantCulture) + Environment.NewLine + filePath);
            }
        }

        /// <summary>
        /// Get an MD5 hased filename to store the last read byte
        /// http://msdn.microsoft.com/en-us/library/system.security.cryptography.md5.aspx
        /// </summary>
        public static string GetMD5HashFileName(HashAlgorithm hashAlgorithm, string input)
        {
            //Validate inputs
            if (hashAlgorithm == null)
            {
                throw new ArgumentNullException("hashAlgorithm", "Hash algorithm cannot be null");
            }
            if (String.IsNullOrWhiteSpace(input))
            {
                throw new ArgumentNullException("input", "Input cannot be null");
            }

            // Convert the input string to a byte array and compute the hash.
            var data = hashAlgorithm.ComputeHash(Encoding.UTF8.GetBytes(input));

            // Create a new Stringbuilder to collect the bytes
            // and create a string.
            var sBuilder = new StringBuilder();

            // Loop through each byte of the hashed data 
            // and format each one as a hexadecimal string.
            for (int i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            // Return the hexadecimal string.
            return sBuilder + ".offset";
        }


        public void GetMetrics(Action<IEnumerable<Metric>> sendMetrics)
        {
            Parallel.ForEach(logs, log =>
                                       {
                                           foreach (var locationKey in log.Locations.AllKeys)
                                           {
                                               foreach (var file in Directory.GetFiles(log.Locations[locationKey].Value, log.Pattern))
                                               {
                                                   ReadTail(file, log, locationKey, sendMetrics);
                                               }
                                           }
                                       });

        }

        private static void ReadTail(string file, LogConfigurationElement log, string locationKey, Action<IEnumerable<Metric>> sendMetrics)
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

            var offset = GetLastByteRead(file);
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
                            CollateAndSend(log, rawValues, file, lastPosition, sendMetrics);
                            rawValues.Clear();
                            lineCount = 0;
                        }
                    }
                }
            }
            finally
            {
                if (stream != null)
                {
                    stream.Dispose();
                }
            }

            CollateAndSend(log, rawValues, file, lastPosition, sendMetrics);
        }

        private static void CollateAndSend(LogConfigurationElement log, IDictionary<LogStatConfigurationElement, ConcurrentBag<Metric>> rawValues,
            string file, long lastPosition, Action<IEnumerable<Metric>> sendMetrics)
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
                        //case "avg":
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

            StoreLastByteRead(file, lastPosition);
            sendMetrics(metrics);
        }

        private static void ParseLine(LogConfigurationElement log, string line,
            ConcurrentDictionary<LogStatConfigurationElement, ConcurrentBag<Metric>> rawValues, string locationKey)
        {
            var matches = log.CompiledRegex.Matches(line);

            //Loop through all the various stats
            foreach (LogStatConfigurationElement stat in log.Stats)
            {
                if (matches.Count > 0)
                {
                    var key = stat.GraphiteKey.Replace("{locationKey}", locationKey);

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
                        var metric = new Metric
                                         {
                                             Key = key,
                                             Timestamp =
                                                 DateTime.ParseExact(matches[0].Groups[stat.Interval].Value,
                                                                     stat.DateFormat, CultureInfo.InvariantCulture)
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
                        //TODO : send error metric
                    }
                }
            }
        }
    }
}
