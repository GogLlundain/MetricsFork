using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;

namespace Metrics.Parsers.WebPagetest
{
    public class SiteConfigurationElement : ConfigurationElement
    {
        [ConfigurationProperty("enabled", DefaultValue = true, IsRequired = false)]
        public bool Enabled
        {
            get
            {
                return (Boolean)this["enabled"];
            }
            set
            {
                this["enabled"] = value;
            }
        }

        [ConfigurationProperty("allowMultipleRuns", DefaultValue = true, IsRequired = false)]
        public bool AllowMultipleRuns
        {
            get
            {
                return (Boolean)this["allowMultipleRuns"];
            }
            set
            {
                this["allowMultipleRuns"] = value;
            }
        }

        [ConfigurationProperty("graphiteKey", IsRequired = true, IsKey = true, DefaultValue = "site")]
        [StringValidator(InvalidCharacters = "~!@#$%^&*()[]{}/;'\"|\\ ", MinLength = 3, MaxLength = 50)]
        public string GraphiteKey
        {
            get
            {
                return (String)this["graphiteKey"];
            }
            set
            {
                this["graphiteKey"] = value;
            }
        }

        [ConfigurationProperty("urlPrefix", IsRequired = true)]
        public string UrlPrefix
        {
            get
            {
                return (String)this["urlPrefix"];
            }
            set
            {
                this["urlPrefix"] = value;
            }
        }

    }
}
