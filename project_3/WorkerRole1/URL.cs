using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace WorkerRole1 {
    public class URL : TableEntity {

        public string url { get; set; }
        public string title { get; set; }

        public URL(string url, string title) {
            this.PartitionKey = WebCrawler.parseURL(url)[1]; // domain component; e.g. media.cnn.com, www.bleacherreport.com
            this.RowKey = sha256(url); // hash of url

            this.title = title;
            this.Timestamp = DateTime.UtcNow;
            this.url = url;
        }

        // use this to make urls that haven't been visited yet, but have been seen.
        public URL(string url) : this(url, null) { }

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
            return (this.PartitionKey+this.RowKey).GetHashCode();
        }

        public override bool Equals(object obj) {
            var other = obj as URL;
            if (other == null) {
                return false;
            }
            return this.RowKey == other.RowKey;
        }
    }
}
