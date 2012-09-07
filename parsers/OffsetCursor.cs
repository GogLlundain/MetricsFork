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
            this.prefix = prefix;
        }

        public void StoreLastRead(string logName, string uniqueName, T offset, string firstLineHash = null)
        {
            using (var md5 = MD5.Create())
            {
                string offsetFileDirectory = String.Format("offset{0}{1}{0}{2}{0}", Path.DirectorySeparatorChar, prefix, logName);
                CheckDirectory(offsetFileDirectory);
                string tempName = offsetFileDirectory + GetMD5HashFileName(md5, uniqueName);
                //var tempName = "offset" + Path.DirectorySeparatorChar + prefix + Path.DirectorySeparatorChar + prefix + "-" + GetMD5HashFileName(md5, uniqueName);

                string value = GetOffsetValue(offset);
                if (!String.IsNullOrWhiteSpace(firstLineHash))
                {
                    value = value + Environment.NewLine + firstLineHash;
                }

                //if its a string value we don't write the helper name in the file
                if (offset is String)
                {
                    File.WriteAllText(tempName, value);
                }
                else
                {
                    File.WriteAllText(tempName, value + Environment.NewLine + uniqueName);
                }

                //Remember we accessed this file to avoid clean up
                if (!filesUsed.Contains(tempName))
                {
                    filesUsed.Add(tempName);
                }
            }
        }

        private string GetOffsetValue(T offset)
        {
            return Convert.ToString(offset, CultureInfo.InvariantCulture);
        }

        public T GetLastRead(string logName, string uniqueName, string firstLineHash = null)
        {
            try
            {
                using (var md5 = MD5.Create())
                {
                    string offsetFileDirectory = String.Format("offset{0}{1}{0}{2}{0}", Path.DirectorySeparatorChar, prefix, logName);
                    CheckDirectory(offsetFileDirectory);
                    string tempName = offsetFileDirectory + GetMD5HashFileName(md5, uniqueName);

                    if (!File.Exists(tempName))
                    {
                        //Backwards compatibility with old naming
                        tempName = "offset" + Path.DirectorySeparatorChar + prefix + Path.DirectorySeparatorChar + prefix + "-" + GetMD5HashFileName(md5, uniqueName);
                    }

                    if (!File.Exists(tempName))
                    {
                        return default(T);
                    }

                    //Remember we accessed this file to avoid clean up
                    if (!filesUsed.Contains(tempName))
                    {
                        filesUsed.Add(tempName);
                    }

                    var lines = File.ReadAllLines(tempName);

                    //If we have a first line hash, first make sure this file is the same one to handle rolling files
                    if (!String.IsNullOrWhiteSpace(firstLineHash))
                    {
                        if (String.CompareOrdinal(lines[1], firstLineHash) != 0)
                        {
                            return default(T);
                        }
                    }

                    if (typeof(T) == typeof(String))
                    {
                        return ConvertType(String.Join(Environment.NewLine, lines));
                    }
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

        public void CheckDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        /// <summary>
        /// Get an MD5 hased filename to store the last read byte
        /// http://msdn.microsoft.com/en-us/library/system.security.cryptography.md5.aspx
        /// </summary>
        public static string GetMD5HashFileName(HashAlgorithm hashAlgorithm, string input)
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
