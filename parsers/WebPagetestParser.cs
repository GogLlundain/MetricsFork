using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using System.Xml.XPath;
using CsvHelper;
using Metrics.Parsers.WebPagetest;
using PublicSuffix;

namespace Metrics.Parsers
{
    public class WebPagetestParser : IMetricParser
    {
        private static SiteConfigurationSection siteSection;
        private readonly OffsetCursor<string> cursor;
        private static Parser parser;
        private readonly List<string> keysToKeep;

        public WebPagetestParser()
        {
            siteSection = ConfigurationManager.GetSection("WebPagetest") as SiteConfigurationSection;
            cursor = new OffsetCursor<string>("wpt");
            if (siteSection == null)
            {
                throw new InvalidOperationException("No site section found for webpagetest");
            }

            if (String.IsNullOrWhiteSpace(siteSection.KeysToKeep))
            {
                keysToKeep = null;
            }
            else
            {
                keysToKeep = new List<string>(siteSection.KeysToKeep.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            }
        }

        private IEnumerable<Metric> XmlToMetrics(SiteConfigurationElement site, IXPathNavigable result)
        {
            var metrics = new List<Metric>();
            if (result == null)
                throw new ArgumentNullException("result", "WebPagetest result was null");

            var navigator = result.CreateNavigator();

            //Check test is completed
            var code = navigator.SelectSingleNode("response/statusCode");
            if (code.Value == "200")
            {
                foreach (XPathNavigator runNavigator in navigator.Select("response/data/run"))
                {
                    var idNode = runNavigator.SelectSingleNode("id");
                    if (idNode == null) continue;

                    var run = site.AllowMultipleRuns ? "." + idNode.Value : String.Empty;

                    //firstView
                    DoView(runNavigator, "firstView", site, run, metrics, keysToKeep);

                    //repeatView
                    DoView(runNavigator, "repeatView", site, run, metrics, keysToKeep);

                    if (!site.AllowMultipleRuns)
                        break;
                }

                if (site.DetailedMetrics)
                {
                    metrics.AddRange(GetDetailedMerics(navigator, site));
                }
            }

            return metrics;
        }


        private static string GetSubDomain(string url)
        {
            if (parser != null)
            {
                // parse urls into Domain objects
                var domain = parser.Parse(url);
                if (domain.IsValid)
                {
                    return domain.RegisteredDomain;
                }
            }

            return null;
        }

        private static IEnumerable<Metric> GetDetailedMerics(XPathNavigator navigator, SiteConfigurationElement site)
        {
            var metrics = new List<Metric>();

            //Get the test id
            var node = navigator.SelectSingleNode("response/data/testId");
            if (node == null) return metrics;
            string testId = node.Value;

            //Get the test url
            node = navigator.SelectSingleNode("response/data/testUrl");
            if (node == null) return metrics;
            string testUrl = node.Value.Replace("http://", "");

            //Get the run date
            node = navigator.SelectSingleNode("response/data/run//date");
            if (node == null) return metrics;
            string dateTime = node.Value;
            var timeStamp = EpochToDateTime(dateTime, siteSection.AssumeLocalTimeZone);

            var request = WebRequest.Create(String.Format(CultureInfo.InvariantCulture, "{0}/result/{1}/{1}_{2}_requests.csv", siteSection.WebPagetestHost, testId, HttpUtility.UrlEncode(testUrl)));
            request.Timeout = 5000;
            var response = (HttpWebResponse)request.GetResponse();
            var stream = response.GetResponseStream();
            try
            {
                if (stream == null)
                {
                    return metrics;
                }

                using (var reader = new CsvReader(new StreamReader(stream)))
                {
                    stream = null;

                    foreach (var row in reader.GetRecords<DetailedWebpagetestRow>())
                    {
                        if (String.IsNullOrEmpty(row.Host))
                        {
                            continue;
                        }

                        //Extract the host to group by
                        string host = GetSubDomain("http://" + row.Host);
                        if (host == null)
                        {
                            host = row.Host;
                        }
                        host = host.Replace(".", "_");

                        //Get the run type
                        string runType = "firstView";
                        if (row.EventName.StartsWith("Cached"))
                        {
                            runType = "repeatView";
                        }

                        //TTFB - By Host
                        metrics.Add(new Metric
                        {
                            Key = String.Format(CultureInfo.InvariantCulture, "{0}.{1}.byHost.{2}.ttfb", site.GraphiteKey, runType, host),
                            Timestamp = timeStamp,
                            Value = row.TimeToFirstByte
                        });
                        //TTFB - By MIME Type
                        if (!String.IsNullOrEmpty(row.ContentType))
                        {
                            metrics.Add(new Metric
                            {
                                Key = String.Format(CultureInfo.InvariantCulture, "{0}.{1}.byType.{2}.ttfb", site.GraphiteKey, runType, row.ContentType),
                                Timestamp = timeStamp,
                                Value = row.TimeToFirstByte
                            });
                        }

                        //TTL - By Host
                        metrics.Add(new Metric
                        {
                            Key = String.Format(CultureInfo.InvariantCulture, "{0}.{1}.byHost.{2}.ttl", site.GraphiteKey, runType, host),
                            Timestamp = timeStamp,
                            Value = row.TimeToLoad
                        });
                        //TTL - By MIME Type
                        if (!String.IsNullOrEmpty(row.ContentType))
                        {
                            metrics.Add(new Metric
                            {
                                Key = String.Format(CultureInfo.InvariantCulture, "{0}.{1}.byType.{2}.ttl", site.GraphiteKey, runType, row.ContentType),
                                Timestamp = timeStamp,
                                Value = row.TimeToLoad
                            });
                        }

                        //Total bytes - By Host
                        metrics.Add(new Metric
                        {
                            Key = String.Format(CultureInfo.InvariantCulture, "{0}.{1}.byHost.{2}.bytes.count", site.GraphiteKey, runType, host),
                            Timestamp = timeStamp,
                            Value = row.Bytes
                        });
                        //Total bytes - By MIME Type
                        if (!String.IsNullOrEmpty(row.ContentType))
                        {
                            metrics.Add(new Metric
                            {
                                Key = String.Format(CultureInfo.InvariantCulture, "{0}.{1}.byType.{2}.bytes.count", site.GraphiteKey, runType, row.ContentType),
                                Timestamp = timeStamp,
                                Value = row.Bytes
                            });
                        }
                        //Avg bytes - By Host
                        metrics.Add(new Metric
                        {
                            Key = String.Format(CultureInfo.InvariantCulture, "{0}.{1}.byHost.{2}.bytes.avg", site.GraphiteKey, runType, host),
                            Timestamp = timeStamp,
                            Value = row.Bytes
                        });
                        //Avg bytes - By MIME Type
                        if (!String.IsNullOrEmpty(row.ContentType))
                        {
                            metrics.Add(new Metric
                            {
                                Key = String.Format(CultureInfo.InvariantCulture, "{0}.{1}.byType.{2}.bytes.avg", site.GraphiteKey, runType, row.ContentType),
                                Timestamp = timeStamp,
                                Value = row.Bytes
                            });
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

            return metrics;
        }


        private static void DoView(XPathNavigator runNavigator, string view, SiteConfigurationElement site, string run, ICollection<Metric> metrics, ICollection<string> keysToKeep)
        {
            var viewNavigator = runNavigator.SelectSingleNode(view + "/results");
            if (viewNavigator != null)
            {
                var node = viewNavigator.SelectSingleNode("date");
                if (node == null) return;

                string dateTime = node.Value;
                var timeStamp = EpochToDateTime(dateTime, siteSection.AssumeLocalTimeZone);
                foreach (XPathNavigator metric in viewNavigator.SelectChildren(XPathNodeType.Element))
                {
                    if ((keysToKeep != null) && (!keysToKeep.Contains(metric.Name)))
                    {
                        continue;
                    }

                    int numericValue;
                    if (Int32.TryParse(metric.Value, out numericValue))
                    {
                        metrics.Add(new Metric
                                        {
                                            Key =
                                                String.Format(CultureInfo.InvariantCulture, "{0}.{1}{2}.{3}", site.GraphiteKey,
                                                              view, run, metric.Name),
                                            Timestamp = timeStamp,
                                            Value = numericValue
                                        });
                    }
                }
            }
        }

        private static DateTime EpochToDateTime(string epoch, bool assumeLocal)
        {
            var seconds = Int64.Parse(epoch, CultureInfo.InvariantCulture);
            DateTime dt;

            //Seems to be a WPT bug where the epochs are calculated from local not UTC
            if (assumeLocal)
            {
                dt = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Local);
            }
            else
            {
                dt = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            }
            dt = dt.AddSeconds(seconds);
            return dt;
        }

        public IEnumerable<string> GetMetrics(GraphiteClient client)
        {
            //Load the domain parser rule set
            if ((File.Exists("effective_tld_names.dat")) && (parser == null))
            {
                var list = new RulesList(); // get a new rules list
                var rules = list.FromFile(@"effective_tld_names.dat");  // parse a local copy of the publicsuffix.org file
                parser = new Parser(rules); // create an instance of the parser
            }

            foreach (var site in siteSection.Sites.OfType<SiteConfigurationElement>())
            {
                string lastRead = cursor.GetLastRead(site.GraphiteKey);
                Parallel.ForEach(GetResultUrls(site.Url, lastRead), resultUrl =>
                    {
                        if (!String.IsNullOrWhiteSpace(resultUrl))
                        {
                            try
                            {
                                var metrics = XmlToMetrics(site, new XPathDocument(resultUrl));
                                if (metrics.Any())
                                {
                                    client.SendMetrics(metrics);
                                    lock (cursor)
                                    {
                                        lastRead += Environment.NewLine + resultUrl;
                                        cursor.StoreLastRead(site.GraphiteKey, lastRead);
                                    }
                                }
                            }
                            catch
                            {
                                lock (cursor)
                                {
                                    lastRead += Environment.NewLine + resultUrl + Environment.NewLine + "failed:" + resultUrl;
                                    cursor.StoreLastRead(site.GraphiteKey, lastRead);
                                }
                            }
                        }
                    });
            }

            return cursor.GetUsedOffsetFiles();
        }

        private static IEnumerable<string> GetResultUrls(string url, string lastRead)
        {
            List<string> readKeys;
            if (lastRead != null)
            {

                readKeys = new List<string>(lastRead.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
            }
            else
            {
                readKeys = new List<string>();
            }
            var results = new List<string>();

            var request = WebRequest.Create(String.Format("{0}/testlog.php?days=365&private=1&filter={1}&f=csv&all=1&nolimit=1", siteSection.WebPagetestHost, url));

            var response = (HttpWebResponse)request.GetResponse();
            using (var stream = response.GetResponseStream())
            {
                if (stream != null)
                {
                    using (var reader = new CsvReader(new StreamReader(stream)))
                    {
                        foreach (var row in reader.GetRecords<WebpagetestRow>())
                        {
                            string resultUrl = String.Format("{0}/xmlResult/{1}/", siteSection.WebPagetestHost, row.TestId);
                            if (!readKeys.Contains(resultUrl))
                            {
                                results.Add(resultUrl);
                            }
                        }
                    }
                }
            }

            return results;
        }
    }
}
