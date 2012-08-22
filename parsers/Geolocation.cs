using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;

namespace Metrics.Parsers
{
    public class Geolocation
    {
        private static Geolocation instance;
        private static readonly object LockObject = new object();
        private readonly List<Range> lookup;
        private bool useCache = true;
        public bool UseCache
        {
            get
            {
                return useCache;
            }
            set
            {
                useCache = value;
            }
        }

        public static Geolocation Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (LockObject)
                    {
                        instance = new Geolocation();
                    }
                }

                return instance;
            }
        }

        private Geolocation()
        {
            lookup = new List<Range>();
            var locations = new Dictionary<int, Location>();

            foreach (var line in File.ReadAllLines("GeoLiteCity-Location.csv"))
            {
                var split = line.Split(new[] { ',' });
                if (split.Length == 9)
                {
                    try
                    {
                        locations.Add(Int32.Parse(split[0].Replace("\"", "")),
                                      new Location
                                          {
                                              City = split[3].Replace("\"", ""),
                                              Country = split[1].Replace("\"", ""),
                                              State = split[2].Replace("\"", "")
                                          });
                    }
                    catch (FormatException)
                    {
                    }
                }
            }

            foreach (var line in File.ReadAllLines("GeoLiteCity-Blocks.csv"))
            {
                var split = line.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (split.Length == 3)
                {
                    try
                    {
                        var range = new Range
                                        {
                                            Start = Int64.Parse(split[0].Replace("\"", "")),
                                            End = Int64.Parse(split[1].Replace("\"", "")),
                                            IpLocation = locations[Int32.Parse(split[2].Replace("\"", ""))]
                                        };

                        lookup.Add(range);
                    }
                    catch (FormatException)
                    {
                    }
                }
            }
        }

        //Cached locations... in theory on a select set of users use the site so this is effecient enough
        private ConcurrentDictionary<long, Range> cachedLocations = new ConcurrentDictionary<long, Range>();
        public Range GetLocation(IPAddress ip)
        {
            var longAddress = IpAddressToLong(ip);

            if (useCache)
            {
                if (cachedLocations.ContainsKey(longAddress))
                {
                    return cachedLocations[longAddress];
                }
            }

            var result = lookup.AsParallel().FirstOrDefault(range => range.Start <= longAddress && range.End >= longAddress);

            if (useCache)
            {
                cachedLocations.TryAdd(longAddress, result);
            }

            return result;
        }

        public static long IpAddressToLong(IPAddress ip)
        {
            if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                throw new InvalidOperationException("IP address is not IPv4");

            long num = 0;
            byte[] bytes = ip.GetAddressBytes();
            for (int i = 0; i < 4; ++i)
            {
                long y = bytes[i];
                if (y < 0)
                    y += 256;
                num += y << ((3 - i) * 8);
            }

            return num;
        }
    }
}
