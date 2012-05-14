using System;
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

        [ConfigurationProperty("wptHost", IsRequired = true)]
        public string WebPagetestHost
        {
            get
            {
                return (String)this["wptHost"];
            }
            set
            {
                this["wptHost"] = value;
            }
        }

        [ConfigurationProperty("keysToKeep", IsRequired = false)]
        public string KeysToKeep
        {
            get
            {
                return (String)this["keysToKeep"];
            }
            set
            {
                this["keysToKeep"] = value;
            }
        }
    }
}
