using System;
using System.Configuration;
using System.Text.RegularExpressions;
using System.Threading;

namespace Metrics.Parsers.LogTail
{
    public class LogConfigurationElement : ConfigurationElement
    {
        private Regex regex;

        internal Regex CompiledRegex
        {
            get
            {
                if (regex == null)
                {
                    var section = ConfigurationManager.GetSection("LogTail") as LogConfigurationSection;
                    if (section != null)
                    {
                        var compiled = new Regex(section.Patterns[ExpressionKey].Value, RegexOptions.Compiled);
                        Interlocked.CompareExchange(ref regex, compiled, null);
                    }
                }

                return regex;
            }
        }

        [ConfigurationProperty("enabled", DefaultValue = true, IsRequired = false)]
        public bool Enabled
        {
            get { return (Boolean)this["enabled"]; }
            set { this["enabled"] = value; }
        }

        [ConfigurationProperty("pattern", IsRequired = false, DefaultValue = "*")]
        public string Pattern
        {
            get { return (String)this["pattern"]; }
            set { this["pattern"] = value; }
        }

        [ConfigurationProperty("name", IsRequired = true)]
        public string Name
        {
            get { return (String)this["name"]; }
            set { this["name"] = value; }
        }

        [ConfigurationProperty("maxTailMB", IsRequired = false, DefaultValue = 0)]
        public int MaxTailMB
        {
            get { return (int)this["maxTailMB"]; }
            set { this["maxTailMB"] = value; }
        }

        [ConfigurationProperty("maxDaysToProcess", IsRequired = false, DefaultValue = 7)]
        public int MaxDaysToProcess
        {
            get { return (int)this["maxDaysToProcess"]; }
            set { this["maxDaysToProcess"] = value; }
        }

        [ConfigurationProperty("regexKey", IsRequired = true)]
        public string ExpressionKey
        {
            get { return (String)this["regexKey"]; }
            set { this["regexKey"] = value; }
        }

        [ConfigurationProperty("boomerangBeacon", IsRequired = false)]
        public string BoomerangBeacon
        {
            get { return (String)this["boomerangBeacon"]; }
            set { this["boomerangBeacon"] = value; }
        }

        [ConfigurationProperty("boomerangKey", IsRequired = false)]
        public string BoomerangKey
        {
            get { return (String)this["boomerangKey"]; }
            set { this["boomerangKey"] = value; }
        }

        [ConfigurationProperty("interval", IsRequired = false)]
        public string Interval
        {
            get { return (String)this["interval"]; }
            set { this["interval"] = value; }
        }

        [ConfigurationProperty("dateFormat", IsRequired = false, DefaultValue = "yyyy-mm-dd hh:MM:ss")]
        public string DateFormat
        {
            get { return (String)this["dateFormat"]; }
            set { this["dateFormat"] = value; }
        }

        [ConfigurationProperty("rollingFile", IsRequired = false, DefaultValue = false)]
        public bool SingleRollingFile
        {
            get
            {
                return (bool)this["rollingFile"];
            }
            set
            {
                this["rollingFile"] = value;
            }
        }

        [ConfigurationProperty("assumeUTC", IsRequired = false, DefaultValue = true)]
        public bool AssumeUniversal
        {
            get
            {
                return (bool)this["assumeUTC"];
            }
            set
            {
                this["assumeUTC"] = value;
            }
        }

        [ConfigurationProperty("tenSecondGroup", IsRequired = false, DefaultValue = true)]
        public bool TenSecondGroup
        {
            get
            {
                return (bool)this["tenSecondGroup"];
            }
            set
            {
                this["tenSecondGroup"] = value;
            }
        }

        [ConfigurationProperty("Mapping", IsRequired = true)]
        public KeyValueConfigurationCollection Mapping
        {
            get { return (KeyValueConfigurationCollection)this["Mapping"]; }
        }

        [ConfigurationProperty("BoomerangUrls", IsRequired = false)]
        public KeyValueConfigurationCollection BoomerangUrls
        {
            get { return (KeyValueConfigurationCollection)this["BoomerangUrls"]; }
        }

        [ConfigurationProperty("Locations", IsRequired = true)]
        public KeyValueConfigurationCollection Locations
        {
            get { return (KeyValueConfigurationCollection)this["Locations"]; }
        }

        [ConfigurationProperty("Stats", IsRequired = true)]
        public LogStatConfigurationCollection Stats
        {
            get { return (LogStatConfigurationCollection)this["Stats"]; }
        }
    }
}
