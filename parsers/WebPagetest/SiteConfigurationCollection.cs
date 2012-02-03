using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;

namespace Metrics.Parsers.WebPagetest
{
    [ConfigurationCollection(typeof(SiteConfigurationElement), AddItemName = "Site",
        CollectionType = ConfigurationElementCollectionType.BasicMap)]
    public class SiteConfigurationCollection : ConfigurationElementCollection
    {

        protected override ConfigurationElement CreateNewElement()
        {
            return new SiteConfigurationElement();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((SiteConfigurationElement)element).GraphiteKey;
        }

        new public SiteConfigurationElement this[string name]
        {
            get { return (SiteConfigurationElement)BaseGet(name); }
        }
    }
}
