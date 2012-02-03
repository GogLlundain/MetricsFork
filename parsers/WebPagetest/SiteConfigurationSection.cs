using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;

namespace Metrics.Parsers.WebPagetest
{
    public class SiteConfigurationSection : ConfigurationSection
    {
        [ConfigurationProperty("Sites", IsRequired = true)]
        public SiteConfigurationCollection Sites
        {
            get { return (SiteConfigurationCollection)this["Sites"]; }
        }

    }
}
