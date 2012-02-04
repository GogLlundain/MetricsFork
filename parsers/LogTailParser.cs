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

        private long GetLastByteRead(string filePath, LogConfigurationElement log)
        {
            using (var md5 = MD5.Create())
            {
                string tempName = GetMd5HashFileName(md5, filePath + "|" + log.GraphiteKey);
                if (!File.Exists(tempName))
                {
                    return 0;
                }
                else
                {
                    long offset = 0;
                    Int64.TryParse(File.ReadAllText(tempName), out offset);
                    return offset;
                }
            }
        }

        private void StoreLastByteRead(string filePath, LogConfigurationElement log, long offset)
        {
            using (var md5 = MD5.Create())
            {
                string tempName = GetMd5HashFileName(md5, filePath + "|" + log.GraphiteKey);
                File.WriteAllText(tempName, offset.ToString());
            }
        }

        /// <summary>
        /// Get an MD5 hased filename to store the last read byte
        /// http://msdn.microsoft.com/en-us/library/system.security.cryptography.md5.aspx
        /// </summary>
        private static string GetMd5HashFileName(MD5 md5Hash, string input)
        {

            // Convert the input string to a byte array and compute the hash.
            byte[] data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(input));

            // Create a new Stringbuilder to collect the bytes
            // and create a string.
            StringBuilder sBuilder = new StringBuilder();

            // Loop through each byte of the hashed data 
            // and format each one as a hexadecimal string.
            for (int i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2"));
            }

            // Return the hexadecimal string.
            return sBuilder.ToString() + ".offset";
        }


        public IEnumerable<Metric> GetMetrics()
        {
            var metrics = new ConcurrentBag<Metric>();
            Parallel.ForEach(logs.Cast<LogConfigurationElement>(), log =>
                {
                    var metricList = ReadTail(log);
                    Parallel.ForEach(metricList, metric =>
                    {
                        metrics.Add(metric);
                    });
                });

            return metrics;
        }


        private IEnumerable<Metric> ReadTail(LogConfigurationElement log)
        {
            var rawValues = new List<Metric>();
            foreach (var file in Directory.GetFiles(log.Location, log.Pattern))
            {
                var info = new FileInfo(file);
                var offset = GetLastByteRead(file, log);

                //If file hasnt changed, don't bother opening
                if (info.Length <= offset)
                {
                    return rawValues;
                }

                //If the file is greater than our maxTailMB setting, skip to the maximum and proceed
                if (log.MaxTailMB > 0)
                {
                    long maxTailBytes = (long)log.MaxTailMB * 1048576;
                    if (info.Length - offset > maxTailBytes)
                    {
                        offset = info.Length - maxTailBytes;
                    }
                }

                using (var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (var reader = new StreamReader(stream))
                    {
                        reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                        try
                        {
                            while (reader.Peek() != -1)
                            {
                                var line = reader.ReadLine();
                                var metric = ParseLine(log, line);
                                //Null metric means the line did not match the regex
                                if (metric != null)
                                {
                                    rawValues.Add(metric);
                                }
                            }
                        }
                        finally
                        {
                            StoreLastByteRead(file, log, reader.BaseStream.Position);
                        }
                    }
                }
            }

            switch (log.AggregateType)
            {
                case "count":
                    return from value in rawValues
                           group value by new { value.Timestamp, value.Key } into metricGroup
                           select new Metric { Key = metricGroup.Key.Key, Timestamp = metricGroup.Key.Timestamp, Value = metricGroup.Count() };
                case "avg":
                default:
                    return from value in rawValues
                           group value by new { value.Timestamp, value.Key } into metricGroup
                           select new Metric { Key = metricGroup.Key.Key, Timestamp = metricGroup.Key.Timestamp, Value = metricGroup.Sum(metric => metric.Value) / metricGroup.Count() };
            }
        }

        private static Metric ParseLine(LogConfigurationElement log, string line)
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

                var metric = new Metric();
                metric.Key = key;
                metric.Timestamp = DateTime.ParseExact(matches[0].Groups[log.Interval].Value, log.DateFormat,
                             CultureInfo.InvariantCulture);
                if (!String.IsNullOrEmpty(log.Value))
                {
                    metric.Value = Int32.Parse(matches[0].Groups[log.Value].Value);
                }
                return metric;
            }

            return null;
        }
    }
}
