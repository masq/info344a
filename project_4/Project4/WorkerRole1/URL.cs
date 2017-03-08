using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace WorkerRole1 {
    public class URL : TableEntity {

        public static readonly string value_delimiter = "|||||";
        public static readonly string row_delimiter = ";;;;;";

        public string url { get; set; }
        public string title { get; set; }

        public URL(string url, string title, string word) {
            this.PartitionKey = word;
            // ^ some word that is part of the title or the domain component of URL if hasn't been resolved
            this.RowKey = sha256(url); // hash of url
            
            this.title = title;
            this.Timestamp = DateTime.UtcNow;
            this.url = url;
        }

        // use this to make urls that haven't been visited yet, but have been seen.
        public URL(string url) : this(url, null, WebCrawler.parseURL(url)[1]) { }
        // use this to make urls that have been visited, but had errors (errors are saved as their titles)
        public URL(string url, string title) : this(url, title, WebCrawler.parseURL(url)[1]) { }

        public static string sha256(string str) {
            SHA256Managed crypt = new SHA256Managed();
            StringBuilder hash = new StringBuilder();
            byte[] crypto = crypt.ComputeHash(Encoding.UTF8.GetBytes(str), 0, Encoding.UTF8.GetByteCount(str));
            foreach (byte theByte in crypto) {
                hash.Append(theByte.ToString("x2"));
            }
            return hash.ToString();
        }

        // have to have this blank one for use in foreach loop
        public URL() {

        }

        public override int GetHashCode() {
            return (this.PartitionKey + this.RowKey).GetHashCode();
        }

        public override bool Equals(object obj) {
            var other = obj as URL;
            if (other == null) {
                return false;
            }
            return (this.PartitionKey == other.PartitionKey) && (this.RowKey == other.RowKey);
        }
    }
}
