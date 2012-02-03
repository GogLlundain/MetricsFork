using System;
using System.Xml.XPath;
using System.Configuration;

namespace Metrics.Parsers
{
    public class WebPagetestXmlParser
    {
        public void XmlToGraphite(string site, bool allowMultipleRuns, IXPathNavigable result)
        {
            if (result == null)
                throw new ArgumentNullException("result", "WebPagetest result was null");

            //TODO : check status codes

            var navigator = result.CreateNavigator();
            foreach (XPathNavigator runNavigator in navigator.Select("response/data/run"))
            {
                string run = allowMultipleRuns ? "." + runNavigator.SelectSingleNode("id").Value : String.Empty;

                //firstView
                DoView(runNavigator, "firstView", site, run);

                //repeatView
                DoView(runNavigator, "repeatView", site, run);

                if (!allowMultipleRuns)
                    break;
            }
        }

        private void DoView(XPathNavigator runNavigator, string view, string site, string run)
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
                        SendStat(String.Format("{0}.{1}.{2}.{3}", ConfigurationManager.AppSettings["GraphiteKeyPrefix"],
                            site + run, view, metric.Name), EpochToDateTime(dateTime), numericValue);
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

        private void SendStat(string key, DateTime time, int value)
        {
            /*using (var client = new GraphiteTcpClient("ec2-176-34-164-231.eu-west-1.compute.amazonaws.com", 8080))
            {
                client.Send(key, value, time);
            }*/

            Console.WriteLine("{0} - {1}", key, value);
        }
    }
}
