using System;

namespace Metrics.Parsers
{
    public class Location
    {
        public string Country { get; set; }
        public string State { get; set; }
        public string City { get; set; }

        public override string ToString()
        {
            var name = Country;
            if (!String.IsNullOrWhiteSpace(State))
            {
                name += "." + State;
            }

            return name;
        }
    }
}
