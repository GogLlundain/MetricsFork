using System;
using System.Configuration;

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

        [ConfigurationProperty("interval", IsRequired = false)]
        public string Interval
        {
            get
            {
                return (String)this["interval"];
            }
            set
            {
                this["interval"] = value;
            }
        }

        [ConfigurationProperty("dateFormat", IsRequired = false, DefaultValue = "yyyy-mm-dd hh:MM:ss")]
        public string DateFormat
        {
            get
            {
                return (String)this["dateFormat"];
            }
            set
            {
                this["dateFormat"] = value;
            }
        }

        [ConfigurationProperty("graphiteKey", IsRequired = true, IsKey = true, DefaultValue = "site")]
        [StringValidator(InvalidCharacters = "~!@#$%^&*()[]/;'\"|\\ ", MinLength = 3, MaxLength = 50)]
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
