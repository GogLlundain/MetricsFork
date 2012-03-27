using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Linq;

namespace Metrics.Parsers
{
    public class OffsetCursor<T>
    {
        private readonly ConcurrentBag<string> filesUsed;
        private readonly string prefix;

        public OffsetCursor(string prefix)
        {
            if (String.IsNullOrWhiteSpace(prefix))
            {
                throw new ArgumentNullException("prefix", "Must specify a prefix for the offset files");
            }

            filesUsed = new ConcurrentBag<string>();
            this.prefix = prefix + "_";
        }

        public void StoreLastRead(string uniqueName, T offset)
        {
            using (var md5 = MD5.Create())
            {
                var tempName = prefix + GetMD5HashFileName(md5, uniqueName);

                File.WriteAllText(tempName, GetOffsetValue(offset) + Environment.NewLine + uniqueName);

                //Remember we accessed this file to avoid clean up
                filesUsed.Add(tempName);
            }
        }

        private string GetOffsetValue(T offset)
        {
            return Convert.ToString(offset, CultureInfo.InvariantCulture);
        }

        public T GetLastRead(string uniqueName)
        {
            try
            {
                using (var md5 = MD5.Create())
                {
                    string tempName = prefix + GetMD5HashFileName(md5, uniqueName);

                    if (!File.Exists(tempName))
                    {
                        return default(T);
                    }

                    //Remember we accessed this file to avoid clean up
                    filesUsed.Add(tempName);

                    var lines = File.ReadAllLines(tempName);

                    return ConvertType(lines[0]);
                }
            }
            catch
            {
                return default(T);
            }
        }

        private T ConvertType(string input)
        {
            return (T)Convert.ChangeType(input, typeof(T), CultureInfo.InvariantCulture);
        }

        public IEnumerable<string> GetUsedOffsetFiles()
        {
            return filesUsed.Distinct().ToArray();
        }

        /// <summary>
        /// Get an MD5 hased filename to store the last read byte
        /// http://msdn.microsoft.com/en-us/library/system.security.cryptography.md5.aspx
        /// </summary>
        private static string GetMD5HashFileName(HashAlgorithm hashAlgorithm, string input)
        {
            //Validate inputs
            if (hashAlgorithm == null)
            {
                throw new ArgumentNullException("hashAlgorithm", "Hash algorithm cannot be null");
            }
            if (String.IsNullOrWhiteSpace(input))
            {
                throw new ArgumentNullException("input", "Input cannot be null");
            }

            // Convert the input string to a byte array and compute the hash.
            var data = hashAlgorithm.ComputeHash(Encoding.UTF8.GetBytes(input));

            // Create a new Stringbuilder to collect the bytes
            // and create a string.
            var sBuilder = new StringBuilder();

            // Loop through each byte of the hashed data 
            // and format each one as a hexadecimal string.
            for (int i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            // Return the hexadecimal string.
            return sBuilder + ".offset";
        }
    }
}
