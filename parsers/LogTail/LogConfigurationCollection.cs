using System.Collections.Generic;
using System.Configuration;

namespace Metrics.Parsers.LogTail
{
    [ConfigurationCollection(typeof(LogConfigurationCollection), AddItemName = "Log",
        CollectionType = ConfigurationElementCollectionType.BasicMap)]
    public class LogConfigurationCollection : ConfigurationElementCollection, IEnumerable<LogConfigurationElement>
    {

        protected override ConfigurationElement CreateNewElement()
        {
            return new LogConfigurationElement();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((LogConfigurationElement)element).Name + ((LogConfigurationElement)element).Pattern;
        }

        new public LogConfigurationElement this[string name]
        {
            get { return (LogConfigurationElement)BaseGet(name); }
        }

        public new IEnumerator<LogConfigurationElement> GetEnumerator()
        {
            foreach (var key in base.BaseGetAllKeys())
            {
                yield return BaseGet(key) as LogConfigurationElement;
            }
        }
    }
}
