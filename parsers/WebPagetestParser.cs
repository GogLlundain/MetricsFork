using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.XPath;
using System.Configuration;
using CsvHelper;
using Metrics.Parsers.WebPagetest;
using PublicSuffix;

namespace Metrics.Parsers
{
    public class WebPagetestParser : IMetricParser
    {
        private static SiteConfigurationSection siteSection;
        private static readonly Regex DetailedRegexStatement = new Regex(@"^[^,]+,[^,]+,""Cleared\sCache-Run[^,]+,[^,]+,[^,]+,""(?<host>[^,]+)"",[^,]+,[^,]+,""(?<ttl>[^,]+)"",""(?<ttfb>[^,]+)"",[^,]+,[^,]+,""(?<bytes>[^,]+)""", RegexOptions.Compiled);
        private readonly OffsetCursor<DateTime> cursor;
        private static Parser parser;

        public WebPagetestParser()
        {
            siteSection = ConfigurationManager.GetSection("WebPagetest") as SiteConfigurationSection;
            cursor = new OffsetCursor<DateTime>("wpt");
            if (siteSection == null)
            {
                throw new InvalidOperationException("No site section found for webpagetest");
            }

        }

        private static IEnumerable<Metric> XmlToMetrics(SiteConfigurationElement site, IXPathNavigable result)
        {
            var metrics = new List<Metric>();
            if (result == null)
                throw new ArgumentNullException("result", "WebPagetest result was null");

            //TODO : check status codes

            var navigator = result.CreateNavigator();
            foreach (XPathNavigator runNavigator in navigator.Select("response/data/run"))
            {
                var idNode = runNavigator.SelectSingleNode("id");
                if (idNode == null) continue;

                var run = site.AllowMultipleRuns ? "." + idNode.Value : String.Empty;

                //firstView
                DoView(runNavigator, "firstView", site, run, metrics);

                //repeatView
                DoView(runNavigator, "repeatView", site, run, metrics);

                if (!site.AllowMultipleRuns)
                    break;
            }

            if (site.DetailedMetrics)
            {
                metrics.AddRange(GetDetailedMerics(navigator, site));
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

            var request = WebRequest.Create(String.Format(CultureInfo.InvariantCulture, "{0}/result/{1}/{1}_{2}_requests.csv", siteSection.WebPagetestHost, testId, testUrl));

            var response = (HttpWebResponse)request.GetResponse();
            var stream = response.GetResponseStream();
            try
            {
                if (stream == null)
                {
                    return metrics;
                }

                using (var reader = new StreamReader(stream))
                {
                    //null original variable to prevent double disposal
                    stream = null;

                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();

                        //Ignore blank lines
                        if (String.IsNullOrWhiteSpace(line)) continue;

                        var matches = DetailedRegexStatement.Matches(line);

                        //Ignore lines that did not match
                        if (matches.Count <= 0) continue;

                        //Extract the host to group by
                        string host = GetSubDomain("http://" + matches[0].Groups["host"].Value);
                        if (host == null)
                        {
                            host = matches[0].Groups["host"].Value;
                        }
                        host = host.Replace(".", "_");

                        int value;

                        //TTFB
                        if (!Int32.TryParse(matches[0].Groups["ttfb"].Value, out value))
                        {
                            value = 0;
                        }
                        metrics.Add(new Metric
                                        {
                                            Key = String.Format(CultureInfo.InvariantCulture, "{0}.firstView.hosts.{1}.ttfb.avg", site.GraphiteKey,
                                                                host),
                                            Timestamp = EpochToDateTime(dateTime),
                                            Value = value
                                        });
                        //TTL
                        if (!Int32.TryParse(matches[0].Groups["ttl"].Value, out value))
                        {
                            value = 0;
                        }

                        metrics.Add(new Metric
                                        {
                                            Key = String.Format(CultureInfo.InvariantCulture, "{0}.firstView.hosts.{1}.ttl.avg", site.GraphiteKey,
                                                                host),
                                            Timestamp = EpochToDateTime(dateTime),
                                            Value = value
                                        });
                        //Total bytes
                        if (!Int32.TryParse(matches[0].Groups["bytes"].Value, out value))
                        {
                            value = 0;
                        }

                        metrics.Add(new Metric
                                        {
                                            Key = String.Format(CultureInfo.InvariantCulture, "{0}.firstView.hosts.{1}.bytes.count", site.GraphiteKey,
                                                                host),
                                            Timestamp = EpochToDateTime(dateTime),
                                            Value = value
                                        });
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


        private static void DoView(XPathNavigator runNavigator, string view, SiteConfigurationElement site, string run, List<Metric> metrics)
        {
            var viewNavigator = runNavigator.SelectSingleNode(view + "/results");
            if (viewNavigator != null)
            {
                var node = viewNavigator.SelectSingleNode("date");
                if (node == null) return;

                string dateTime = node.Value;
                foreach (XPathNavigator metric in viewNavigator.SelectChildren(XPathNodeType.Element))
                {
                    int numericValue;
                    if (Int32.TryParse(metric.Value, out numericValue))
                    {
                        metrics.Add(new Metric
                                        {
                                            Key =
                                                String.Format(CultureInfo.InvariantCulture, "{0}.{1}{2}.{3}", site.GraphiteKey,
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
            var seconds = Int64.Parse(epoch, CultureInfo.InvariantCulture);
            var dt = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
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

            foreach (SiteConfigurationElement site in siteSection.Sites)
            {
                foreach (var resultUrl in GetResultUrls(site.Url, cursor.GetLastRead(site.GraphiteKey)))
                {
                    try
                    {
                        var metrics = XmlToMetrics(site, new XPathDocument(resultUrl));
                        client.SendMetrics(metrics);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        cursor.StoreLastRead(site.GraphiteKey, DateTime.Now);
                    }
                }
            }

            return cursor.GetUsedOffsetFiles();
        }

        private static IEnumerable<string> GetResultUrls(string url, DateTime lastRead)
        {
            var difference = Math.Ceiling(DateTime.Now.Subtract(lastRead).TotalDays);
            if (difference > 365)
                difference = 365;

            var request = WebRequest.Create(String.Format("{0}/testlog.php?days={1:F0}&private=1&filter={2}&f=csv", siteSection.WebPagetestHost, (int)difference, url));

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
                                yield return String.Format("{0}/xmlResult/{1}/", siteSection.WebPagetestHost, row.TestId);
                            }
                        }
                    }
                }
            }

            yield return null;
        }
    }
}
