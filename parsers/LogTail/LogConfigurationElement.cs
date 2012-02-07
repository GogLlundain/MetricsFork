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
                    var compiled = new Regex(Expression, RegexOptions.Compiled);
                    Interlocked.CompareExchange(ref regex, compiled, null);
                }

                return regex;
            }
        }

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

        [ConfigurationProperty("location", IsRequired = false)]
        public string Location
        {
            get
            {
                return (String)this["location"];
            }
            set
            {
                this["location"] = value;
            }
        }

        [ConfigurationProperty("pattern", IsRequired = false, DefaultValue = "*")]
        public string Pattern
        {
            get
            {
                return (String)this["pattern"];
            }
            set
            {
                this["pattern"] = value;
            }
        }

        [ConfigurationProperty("maxTailMB", IsRequired = false, DefaultValue = 0)]
        public int MaxTailMB
        {
            get
            {
                return (int)this["maxTailMB"];
            }
            set
            {
                this["maxTailMB"] = value;
            }
        }

        [ConfigurationProperty("regex", IsRequired = true)]
        public string Expression
        {
            get
            {
                return (String)this["regex"];
            }
            set
            {
                this["regex"] = value;
            }
        }

        [ConfigurationProperty("Mapping", IsRequired = true)]
        public KeyValueConfigurationCollection Mapping
        {
            get { return (KeyValueConfigurationCollection)this["Mapping"]; }
        }

        [ConfigurationProperty("Stats", IsRequired = true)]
        public LogStatConfigurationCollection Stats
        {
            get { return (LogStatConfigurationCollection)this["Stats"]; }
        }
    }
}
