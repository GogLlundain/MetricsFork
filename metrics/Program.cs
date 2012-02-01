using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Metrics.Parsers.WebPagetest;
using System.Configuration;
using System.Xml.XPath;

namespace Metrics
{
    class Program
    {
        static void Main(string[] args)
        {
            XPathDocument doc = new XPathDocument("");
            WebPagetestXmlParser wpt = new WebPagetestXmlParser();
            wpt.XmlToGraphite("test", false, doc);

            Console.ReadKey();
        }
    }
}
