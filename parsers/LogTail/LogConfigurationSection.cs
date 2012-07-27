using System.Configuration;

namespace Metrics.Parsers.LogTail
{
    public class LogConfigurationSection : ConfigurationSection
    {
        [ConfigurationProperty("Patterns", IsRequired = true)]
        public KeyValueConfigurationCollection Patterns
        {
            get { return (KeyValueConfigurationCollection)this["Patterns"]; }
        }

        [ConfigurationProperty("Logs", IsRequired = true)]
        public LogConfigurationCollection Logs
        {
            get { return (LogConfigurationCollection)this["Logs"]; }
        }
    }
}
