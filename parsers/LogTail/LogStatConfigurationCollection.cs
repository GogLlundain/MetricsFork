using System.Configuration;

namespace Metrics.Parsers.LogTail
{
    [ConfigurationCollection(typeof(LogStatConfigurationElement), AddItemName = "Stat",
        CollectionType = ConfigurationElementCollectionType.BasicMap)]
    public class LogStatConfigurationCollection : ConfigurationElementCollection
    {

        protected override ConfigurationElement CreateNewElement()
        {
            return new LogStatConfigurationElement();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((LogStatConfigurationElement)element).GraphiteKey;
        }

        new public LogStatConfigurationElement this[string name]
        {
            get { return (LogStatConfigurationElement)BaseGet(name); }
        }
    }
}
