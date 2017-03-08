using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Services;
using System.Web.Services;
using System.Web.Caching;
using WorkerRole1;

namespace WebRole1 {
    /// <summary>
    /// Summary description for Admin
    /// </summary>
    [WebService(Namespace = "http://tempuri.org/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [System.ComponentModel.ToolboxItem(false)]
    // To allow this Web Service to be called from script, using ASP.NET AJAX, uncomment the following line. 
    [System.Web.Script.Services.ScriptService]
    public class Admin : System.Web.Services.WebService {

        private readonly static Storage store = Storage.instance;
        private readonly static int pagination = 20;

        [WebMethod]
        public bool CrawlerCommand(string cmd) { // TODO: consider placing an ID here to control which crawler we're talking to.
            CloudQueueMessage message = new CloudQueueMessage(cmd);
            store.commandq.AddMessageAsync(message);
            return true;
        }

        [WebMethod]
        public string[] GetInfoOnURL(string url) {
            return store.getURLDataSync(url);
        }

        [WebMethod]
        public bool setDomains(string[] domains) {
            WorkerRole.domains = domains;
            return true;
        }

        [WebMethod] // TODO: this is for testing
        public bool crawlURL(string url) {
            store.earlQ.ClearAsync();
            store.earlQ.AddMessageAsync(new CloudQueueMessage(url));
            CrawlerCommand("resume");
            return true;
        }

        [WebMethod]
        public List<string> getOutput() {
            return store.getReports(32);
        }

        [WebMethod]
        public Stats[] getRecentStats() {
            TableQuery<Stats> rangeQuery = new TableQuery<Stats>()
                .Where(getRecentFilter(WorkerRole.report_threshhold_in_seconds * 6)) // get 1 minute back
                .Take(10);
            Stats[] results = null;
            try {
                results = store.stats.ExecuteQuery(rangeQuery).ToArray();
            } catch (Exception e) {
                Debug.WriteLine("[-] Error Retrieving stats: " + e.ToString());
            }
            return results;
        }

        [WebMethod]
        public Errors[] getRecentErrors() {
            TableQuery<Errors> rangeQuery = new TableQuery<Errors>()
                .Where(getRecentFilter(WorkerRole.report_threshhold_in_seconds * 6 * 60 * 24)); // get 1 day back
            Errors[] results = null;
            try {
                results = store.errors.ExecuteQuery(rangeQuery).ToArray();
            } catch (Exception e) {
                Debug.WriteLine("[-] Irony Error: Error getting errors from Error table: " + e.ToString());
            }
            return results;
        }

        private string getRecentFilter(int seconds_back) {
            DateTime dt = DateTime.UtcNow;
            DateTime dt_back = dt.AddSeconds((seconds_back * -1.0));
            string[] tmp = dt.ToString("s").Split('T');
            string pk = tmp[0]; // YYYY-MM-DD
            string rk = tmp[1]; // HH:MM:SS

            string filter_to_part_row = TableQuery.CombineFilters(
                TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.LessThanOrEqual, pk),
                TableOperators.And,
                TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.LessThanOrEqual, rk)
            );
            string filter_to_time_range = TableQuery.CombineFilters(
                TableQuery.GenerateFilterConditionForDate("Timestamp", QueryComparisons.LessThanOrEqual, dt),
                TableOperators.And,
                TableQuery.GenerateFilterConditionForDate("Timestamp", QueryComparisons.GreaterThanOrEqual, dt_back)
            );
            return TableQuery.CombineFilters(filter_to_part_row, TableOperators.And, filter_to_time_range);
        }

        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public string search(string search, int page) {
            List<URL> sorted = null;
            StringBuilder results = new StringBuilder();
            int total = 0;
            if (HttpRuntime.Cache.Get(search) != null) {
                List<List<URL>> pages = ((List<List<URL>>) HttpRuntime.Cache.Get(search));
                if (pages.Count > 0) {
                    List<URL> tmp = null;
                    try {
                        tmp = pages[page];
                        sorted = tmp;
                    } catch (Exception e) {
                        Debug.WriteLine("[-] Search Cache Error: Page doesn't exist. " + e.ToString());
                        sorted = pages[pages.Count - 1];
                    }
                } else {
                    sorted = new List<URL>();
                }
                total = pages.Count * pagination;
            } else {
                List<URL> holder = new List<URL>();
                List<string> words = new List<string>();
                Scanner scan = new Scanner(search.ToLower().Trim());
                while (scan.hasNext()) {
                    string next = scan.next().Trim();
                    if (!char.IsLetterOrDigit(next[0])) {
                        next = next.Substring(1);
                    }
                    if (next.Length > 0) {
                        if (!char.IsLetterOrDigit(next[next.Length - 1])) {
                            next = next.Substring(0, next.Length - 1);
                        }
                        words.Add(next);
                        string hash = URL.sha256(next);
                        List<URL> tmp = store.search(hash);
                        total += tmp.Count;
                        if (tmp != null) {
                            holder.AddRange(tmp);
                        } else {
                            Debug.WriteLine("[-] Nothing found for keyword " + next);
                            store.reportToWebRole("[-] Nothing found for keyword " + next);
                        }
                    }
                }

                // sort pages based on counts of keywords in title
                sorted = holder.Select(x => new KeyValuePair<URL, int>(x, (x.title.ToLower().Split(' ')
                    .Select(tword => words.Where(w => tword.Contains(w)).Count()).Sum())))
                    .OrderByDescending(kvp => kvp.Value).Select(u => u.Key).ToList();

                // makes pages based on pagination constant
                List<List<URL>> pages = new List<List<URL>>();
                for (int i = 0; i < total; i += pagination) {
                    pages.Add(sorted.GetRange(i, (i + pagination > sorted.Count - 1 ? (sorted.Count - 1) - i : pagination)));
                }
                // add to cache, expires if not looked at in 5 minutes
                HttpRuntime.Cache.Insert(search, pages, null, Cache.NoAbsoluteExpiration, TimeSpan.FromMinutes(5));
                try {
                    sorted = pages[0];
                } catch (Exception e) {
                    Debug.WriteLine("[-] Error in search function, no page 0: " + e.ToString());
                    return "";
                }
            }
            results.Append("" + total + URL.row_delimiter);
            foreach (URL url in sorted) {
                results.Append(url.url + URL.value_delimiter + url.title + URL.value_delimiter + url.Timestamp.DateTime.ToString("o") + URL.row_delimiter);
            }
            results.Remove((results.Length - URL.row_delimiter.Length), URL.row_delimiter.Length);
            return results.ToString();
        }

        private static PerformanceCounter _PC = new PerformanceCounter("Memory", "Available MBytes");
        private static Trie _Trey = null;

        // TODO: when going to prod, flip to false
        private static bool _Debug = false;

        [WebMethod]
        public float MemTest() {
            return _PC.NextValue();
        }

        private static bool building = false;

        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public string[] Query(string word) {
            // if something goes wrong... just rebuild trie when user queries
            if (_Trey == null && !building) {
                building = true;
                bool complete = buildTrie();
                building = false;
                if (_Debug) {
                    Debug.WriteLine("[+] TRIE STATUS: " + complete);
                }
            }

            // format to get any bad characters away
            StringBuilder sb = new StringBuilder();
            foreach (char c in word.ToLower().TrimStart()) {
                if (char.IsLetter(c) || c == ' ') sb.Append(c); // check to see if it's a valid character
                else sb.Append(' '); // otherwise treat it as a space
            }
            word = sb.ToString();

            List<string> results = _Trey.GetSuggestions(word, "");
            if (_Debug) {
                Debug.Write("results: [");
                foreach (var result in results) {
                    Debug.Write(result + ", ");
                }
                Debug.WriteLine("]");
            }
            return results.ToArray();
        }

        [WebMethod]
        public bool build() {
            return buildTrie();
        }

        private bool buildTrie() {
            Boolean result = false;
            string file = DownloadFile();
            if (file != null) {
                _Trey = new Trie();
                char prev = '\x00';
                foreach (String line in File.ReadLines(file)) {
                    string tmp = line.Trim().ToLower();
                    _Trey.AddWord(tmp);
                    //if (_Debug) {
                        if (prev != tmp[0]) {
                            prev = tmp[0];
                            Debug.WriteLine("[*] Adding words starting with '" + ("" + tmp[0]).ToUpper() + "' to TRIE");
                        }
                        // TODO: VERY IMPORTANT THAT THIS IS COMMENTED OUT IN PRODUCTION
                        //if (prev > 'b') {
                        //    Debug.WriteLine("[!] Breaking at '" + prev + "' for debugging purposes!");
                        //    break;
                        //}
                    //}
                    // If we're running out of memory... break.
                    if (_PC.NextValue() <= 20) {
                        break;
                    }
                }
                result = true;
            } else {
                result = false;
            }
            return result;
        }

        public string DownloadFile() {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                ConfigurationManager.AppSettings["StorageConnectionString"]);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("swalden1");

            if (container.Exists()) {
                foreach (IListBlobItem item in container.ListBlobs(null, false)) {
                    if (item.GetType() == typeof(CloudBlockBlob)) {
                        CloudBlockBlob blob = (CloudBlockBlob)item;

                        try {
                            var filename = Path.GetTempFileName();
                            File.WriteAllText(filename, blob.DownloadText());
                            return filename;
                        } catch (IOException e) {
                            Debug.WriteLine("[-] Concurrent Write Error during DownloadFile; try again later");
                            Debug.WriteLine(e);
                        }
                    }
                }
            }
            return null;
        }
    }
}
