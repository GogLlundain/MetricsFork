using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Web;
using System.Xml.XPath;
using System.Configuration;
using CsvHelper;
using Metrics.Parsers.WebPagetest;

namespace Metrics.Parsers
{
    public class WebPagetestParser : IMetricParser
    {

        private static SiteConfigurationSection siteSection;
        public WebPagetestParser()
        {
            siteSection = ConfigurationManager.GetSection("WebPagetest") as SiteConfigurationSection;
            if (siteSection == null)
            {
                throw new InvalidOperationException("No site section found for webpagetest");
            }
        }

        private IEnumerable<Metric> XmlToMetrics(SiteConfigurationElement site, IXPathNavigable result)
        {
            var metrics = new List<Metric>();

            if (result == null)
                throw new ArgumentNullException("result", "WebPagetest result was null");

            //TODO : check status codes

            var navigator = result.CreateNavigator();
            foreach (XPathNavigator runNavigator in navigator.Select("response/data/run"))
            {
                string run = site.AllowMultipleRuns ? "." + runNavigator.SelectSingleNode("id").Value : String.Empty;

                //firstView
                DoView(runNavigator, "firstView", site, run, metrics);

                //repeatView
                DoView(runNavigator, "repeatView", site, run, metrics);

                if (!site.AllowMultipleRuns)
                    break;
            }

            return metrics;
        }

        private void DoView(XPathNavigator runNavigator, string view, SiteConfigurationElement site, string run, ICollection<Metric> metrics)
        {
            var viewNavigator = runNavigator.SelectSingleNode(view + "/results");
            if (viewNavigator != null)
            {
                string dateTime = viewNavigator.SelectSingleNode("date").Value;
                foreach (XPathNavigator metric in viewNavigator.SelectChildren(XPathNodeType.Element))
                {
                    int numericValue;
                    if (Int32.TryParse(metric.Value, out numericValue))
                    {
                        metrics.Add(new Metric
                                        {
                                            Key =
                                                String.Format("{0}.{1}{2}.{3}", site.GraphiteKey,
                                                              view, run, metric.Name),
                                            Timestamp = EpochToDateTime(dateTime),
                                            Value = numericValue
                                        });
                    }
                }
            }
        }

        private static DateTime EpochToDateTime(string epoch)
        {
            var seconds = Int64.Parse(epoch);
            var dt = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dt = dt.AddSeconds(seconds);
            return dt;
        }

        private DateTime GetLastDateRead(string graphiteKey)
        {
            using (var md5 = MD5.Create())
            {
                var tempName = LogTailParser.GetMd5HashFileName(md5, graphiteKey);
                if (!File.Exists(tempName))
                {
                    return DateTime.MinValue;
                }

                DateTime lastRead;
                DateTime.TryParse(File.ReadAllText(tempName), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,  out lastRead);
                return lastRead;
            }
        }

        private void StoreLastDateRead(string graphiteKey, DateTime lastRead)
        {
            using (var md5 = MD5.Create())
            {
                var tempName = LogTailParser.GetMd5HashFileName(md5, graphiteKey);
                File.WriteAllText(tempName, lastRead.ToString(CultureInfo.InvariantCulture));
            }
        }

        public IEnumerable<Metric> GetMetrics()
        {
            var metrics = new List<Metric>();
            foreach (SiteConfigurationElement site in siteSection.Sites)
            {
                foreach (var resultUrl in GetResultUrls(site.Url, GetLastDateRead(site.GraphiteKey)))
                {
                    try
                    {
                        metrics.AddRange(XmlToMetrics(site, new XPathDocument(resultUrl)));
                    }
                    catch
                    {
                    }
                    finally
                    {
                        StoreLastDateRead(site.GraphiteKey, DateTime.Now);
                    }
                }
            }

            return metrics;
        }

        private static IEnumerable<string> GetResultUrls(string url, DateTime lastRead)
        {
            double difference = Math.Ceiling(DateTime.Now.Subtract(lastRead).TotalDays);
            if (difference > 365)
                difference = 365;

            var request = WebRequest.Create(String.Format(siteSection.WebPagetestHost + "/testlog.php?days={0:F0}&private=1&filter={1}&f=csv", (int)difference, url));

            var response = (HttpWebResponse)request.GetResponse();
            using (var stream = response.GetResponseStream())
            {
                if (stream != null)
                {
                    using (var reader = new CsvReader(new StreamReader(stream)))
                    {
                        foreach (var row in reader.GetRecords<WebpagetestRow>())
                        {
                            if ((DateTime.ParseExact(row.Timestamp, "MM/dd/yy HH:mm:ss", CultureInfo.InvariantCulture) > lastRead)
                                && (String.Compare(row.Url, url, StringComparison.InvariantCultureIgnoreCase) == 0))
                            {
                                yield return String.Format("http://10.55.72.184:4001/xmlResult/{0}/", row.TestId);
                            }
                        }
                    }
                }
            }

            yield return null;
        }
    }
}
