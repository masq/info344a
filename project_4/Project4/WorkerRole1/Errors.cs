using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerRole1 {
    public class Errors : TableEntity {

        public static readonly string value_delimiter = "|||||";
        public static readonly string row_delimiter = ";;;;;";

        public string errors { get; set; }

        // need to store 10 most recent URLs, number of URLS
        // there should be a new stats object every 10 seconds or so.
        public Errors(KeyValuePair<string, Exception>[] errors) {
            this.Timestamp = DateTime.UtcNow;
            string[] tmp = this.Timestamp.ToString("s").Split('T');
            this.PartitionKey = tmp[0]; // YYYY-MM-DD
            this.RowKey = tmp[1]; // HH:MM:SS 

            this.errors = convertErrorsToString(errors);
        }

        public string convertErrorsToString(KeyValuePair<string, Exception>[] errors) {
            string[] results = new string[errors.Length];
            int i = 0;
            foreach (var kvp in errors) {
                //Debug.WriteLine("[-] Error to be converted: " + kvp.Key + value_delimiter + kvp.Value.ToString());
                results[i++] = "" + kvp.Key + value_delimiter + kvp.Value.Message.ToString() + value_delimiter + DateTime.UtcNow.ToString("o");
            }
            return String.Join(row_delimiter, results);
        }

        public Errors() {

        }
    }
}