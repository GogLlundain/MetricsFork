using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Metrics.Parsers.LogTail;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

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

        private long GetLastByteRead(string filePath)
        {
            using (var md5 = MD5.Create())
            {
                string tempName = GetMd5HashFileName(md5, filePath);
                if (!File.Exists(tempName))
                {
                    return 0;
                }

                long offset;
                Int64.TryParse(File.ReadAllText(tempName), out offset);
                return offset;
            }
        }

        private void StoreLastByteRead(string filePath, long offset)
        {
            using (var md5 = MD5.Create())
            {
                string tempName = GetMd5HashFileName(md5, filePath);
                File.WriteAllText(tempName, offset.ToString());
            }
        }

        /// <summary>
        /// Get an MD5 hased filename to store the last read byte
        /// http://msdn.microsoft.com/en-us/library/system.security.cryptography.md5.aspx
        /// </summary>
        public static string GetMd5HashFileName(MD5 md5Hash, string input)
        {

            // Convert the input string to a byte array and compute the hash.
            byte[] data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(input));

            // Create a new Stringbuilder to collect the bytes
            // and create a string.
            var sBuilder = new StringBuilder();

            // Loop through each byte of the hashed data 
            // and format each one as a hexadecimal string.
            for (int i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2"));
            }

            // Return the hexadecimal string.
            return sBuilder + ".offset";
        }


        public IEnumerable<Metric> GetMetrics()
        {
            var metrics = new ConcurrentBag<Metric>();

            //Get list of log files to de-dupe in case of multiple types of parsing
            var logsByFile = new Dictionary<string, List<LogConfigurationElement>>();
            foreach (LogConfigurationElement log in logs)
            {
                foreach (var file in Directory.GetFiles(log.Location, log.Pattern))
                {
                    if (!logsByFile.ContainsKey(file))
                    {
                        logsByFile.Add(file, new List<LogConfigurationElement>());
                    }

                    logsByFile[file].Add(log);
                }
            }

            //Loop through all required files and process
            Parallel.ForEach(logsByFile.Keys, key =>
                                                  {
                                                      var metricList = ReadTail(key, logsByFile[key]);
                                                      Parallel.ForEach(metricList, metrics.Add);
                                                  });

            return metrics;
        }


        private IEnumerable<Metric> ReadTail(string file, IEnumerable<LogConfigurationElement> logsForFile)
        {
            var rawValues = new ConcurrentDictionary<LogStatConfigurationElement, ConcurrentBag<Metric>>();
            var metrics = new List<Metric>();

            if ((logsForFile == null) || (!logsForFile.Any()))
            {
                return metrics;
            }

            //If multiple log parsers use the same file, select the maximum size (including infinite)
            long maxTailMB = logsForFile.Count(log => log.MaxTailMB == 0) > 0 ? 0 : logsForFile.Select(log => log.MaxTailMB).Max();

            //Work out the last read position
            var info = new FileInfo(file);
            var offset = GetLastByteRead(file);

            //If file hasnt changed, don't bother opening
            if (info.Length <= offset)
            {
                return metrics;
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
            using (var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (var reader = new StreamReader(stream))
                {
                    reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                    long lastPosition = offset;
                    try
                    {
                        while (reader.Peek() != -1)
                        {
                            var line = reader.ReadLine();

                            //Update the lastPosition counter before we try parse so any failures do not repeat the same line
                            lastPosition = reader.BaseStream.Position;
                            Parallel.ForEach(logsForFile, log =>
                                                       {
                                                           ParseLine(log, line, rawValues);
                                                       });

                        }
                    }
                    finally
                    {
                        //Store where we got up to
                        StoreLastByteRead(file, lastPosition);
                    }
                }
            }

            //Aggregate the metrics by their log aggregation method
            foreach (var log in logsForFile)
            {
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
            }

            return metrics;
        }

        private static void ParseLine(LogConfigurationElement log, string line, 
            ConcurrentDictionary<LogStatConfigurationElement, ConcurrentBag<Metric>> rawValues)
        {
            var matches = log.CompiledRegex.Matches(line);

            //Loop through all the various stats
            foreach (LogStatConfigurationElement stat in log.Stats)
            {
                if (matches.Count > 0)
                {
                    var key = stat.GraphiteKey;

                    //do key replacements
                    foreach (var map in log.Mapping.AllKeys)
                    {
                        if (log.Mapping[map].Value.StartsWith("?"))
                        {
                            key = key.Replace("{" + map + "}",
                                              matches[0].Groups[log.Mapping[map].Value.TrimStart('?')].Value);
                        }
                        else
                        {
                            key = key.Replace("{" + map + "}", log.Mapping[map].Value);
                        }
                    }

                    var metric = new Metric
                                     {
                                         Key = key,
                                         Timestamp = DateTime.ParseExact(matches[0].Groups[stat.Interval].Value, stat.DateFormat, CultureInfo.InvariantCulture)
                                     };
                    if (!String.IsNullOrEmpty(stat.Value))
                    {
                        metric.Value = Int32.Parse(matches[0].Groups[stat.Value].Value);
                    }


                    if (stat.IncludeZeros || (!stat.IncludeZeros && metric.Value > 0))
                    {
                        rawValues.TryAdd(stat, new ConcurrentBag<Metric>());
                        rawValues[stat].Add(metric);
                    }
                }
            }
        }
    }
}
