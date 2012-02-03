using System.Configuration;

namespace Metrics.Parsers.LogTail
{
    public class LogConfigurationSection : ConfigurationSection
    {
        [ConfigurationProperty("Logs", IsRequired = true)]
        public LogConfigurationCollection Logs
        {
            get { return (LogConfigurationCollection)this["Logs"]; }
        }

    }
}
