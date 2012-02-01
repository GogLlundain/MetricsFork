using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;

namespace Metrics.Parsers.WebPagetest
{
    public class SiteSection : ConfigurationSection
    {
        [ConfigurationProperty("Sites", IsRequired = true)]
        public SiteCollection Sites
        {
            get { return (SiteCollection)this["Sites"]; }
        }

    }
}
