using System.Configuration;

namespace Metrics.Parsers.LogTail
{
    [ConfigurationCollection(typeof(LogConfigurationCollection), AddItemName = "Log",
        CollectionType = ConfigurationElementCollectionType.BasicMap)]
    public class LogConfigurationCollection : ConfigurationElementCollection
    {

        protected override ConfigurationElement CreateNewElement()
        {
            return new LogConfigurationElement();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((LogConfigurationElement)element).Location + ((LogConfigurationElement)element).Pattern;
        }

        new public LogConfigurationElement this[string name]
        {
            get { return (LogConfigurationElement)BaseGet(name); }
        }
    }
}
