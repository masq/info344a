using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerRole1 {
    public class Stats : TableEntity {

        public static readonly string value_delimiter = "|||||";
        public static readonly string row_delimiter = ";;;;;";

        public string recents { get; set; }
        public int urlCount { get; set; }
        public int queueSize { get; set; }
        public string cpu { get; set; }
        public string ram { get; set; }
        public byte[] crawlerStates { get; set; }

        // need to store 10 most recent URLs, number of URLS
        // there should be a new stats object every 10 seconds or so.
        public Stats(URL[] recents, int urlCount, int queueSize, string cpu, string ram,
                WorkerRole.STATES[] crawlerStates) {
            this.Timestamp = DateTime.UtcNow;
            string[] tmp = this.Timestamp.ToString("s").Split('T');
            this.PartitionKey = tmp[0]; // YYYY-MM-DD
            this.RowKey = tmp[1]; // HH:MM:SS 

            this.recents = convertURLToString(recents);
            this.urlCount = urlCount;
            this.queueSize = queueSize;
            this.cpu = cpu;
            this.ram = ram;
            this.crawlerStates = convertStateToByte(crawlerStates);
        }

        public string convertURLToString(URL[] recents) {
            string[] results = new string[recents.Length];
            int i = 0;
            foreach (URL url in recents) {
                if (url != null) {
                    //Debug.WriteLine("[-] URL to be converted: " + url.url + value_delimiter + url.title + value_delimiter + url.Timestamp.DateTime.ToBinary());
                    results[i++] = "" + url.url + value_delimiter + url.title + value_delimiter + url.Timestamp.DateTime.ToString("o");
                }
            }
            return String.Join(row_delimiter, results);
        }

        public byte[] convertStateToByte(WorkerRole.STATES[] states) {
            byte[] results = new byte[states.Length];
            int i = 0;
            foreach (var s in states) {
                results[i++] = (byte) s;
            }
            return results;
        }

        public Stats() {

        }
    }
}
