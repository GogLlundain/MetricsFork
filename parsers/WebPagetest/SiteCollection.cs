using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;

namespace Metrics.Parsers.WebPagetest
{
    [ConfigurationCollection(typeof(SiteElement), AddItemName = "Site",
        CollectionType = ConfigurationElementCollectionType.BasicMap)]
    public class SiteCollection : ConfigurationElementCollection
    {

        protected override ConfigurationElement CreateNewElement()
        {
            return new SiteElement();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((SiteElement)element).GraphiteKey;
        }

        new public SiteElement this[string name]
        {
            get { return (SiteElement)BaseGet(name); }
        }
    }
}
