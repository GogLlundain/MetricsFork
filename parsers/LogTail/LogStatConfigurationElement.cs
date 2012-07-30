using System;
using System.Linq;
using System.Configuration;
using System.Collections.Generic;

namespace Metrics.Parsers.LogTail
{
    public class LogStatConfigurationElement : ConfigurationElement
    {
        [ConfigurationProperty("includeZeros", DefaultValue = true, IsRequired = false)]
        public bool IncludeZeros
        {
            get
            {
                return (Boolean)this["includeZeros"];
            }
            set
            {
                this["includeZeros"] = value;
            }
        }

        [ConfigurationProperty("type", IsRequired = false, DefaultValue = "raw")]
        public string AggregateType
        {
            get
            {
                return (String)this["type"];
            }
            set
            {
                this["type"] = value;
            }
        }

        [ConfigurationProperty("calculatePerSecond", IsRequired = false, DefaultValue = false)]
        public bool CalculatePerSecond
        {
            get
            {
                return (bool)this["calculatePerSecond"];
            }
            set
            {
                this["calculatePerSecond"] = value;
            }
        }


        [ConfigurationProperty("value", IsRequired = false, DefaultValue = null)]
        public string Value
        {
            get
            {
                return (String)this["value"];
            }
            set
            {
                this["value"] = value;
            }
        }

        [ConfigurationProperty("extensions", IsRequired = false, DefaultValue = "")]
        public string Extensions
        {
            get
            {
                return (String)this["extensions"];
            }
            set
            {
                this["extensions"] = value;
            }
        }

        public List<string> ExtensionsList
        {
            get
            {
                return
                    ((String)this["extensions"]).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            }
            set
            {
                this["extensions"] = String.Join(",", value);
            }
        }

        [ConfigurationProperty("graphiteKey", IsRequired = true, IsKey = true, DefaultValue = "site")]
        [StringValidator(InvalidCharacters = "~!@#$%^&*()[]/;'\"|\\ ", MinLength = 3, MaxLength = 100)]
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
    }
}
