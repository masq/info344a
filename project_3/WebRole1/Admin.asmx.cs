using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Services;
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

        [WebMethod]
        public WorkerRole.STATES getState() {
            return WorkerRole.getState();
        }

        [WebMethod] // TODO: this is for testing
        public bool crawlURL(string url) {
            store.earlQ.ClearAsync();
            store.earlQ.AddMessageAsync(new CloudQueueMessage(url));
            CrawlerCommand("resume");
            return true;
        }

        [WebMethod]
        public int getQueueSize() {
            return WorkerRole.getQueueSize().Result;
        }

        [WebMethod]
        public Stats[] getRecentStats() {
            DateTime dt = DateTime.UtcNow;
            DateTime dt_back = dt.AddSeconds(-10.0 * WorkerRole.report_threshhold_in_seconds);
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
            TableQuery<Stats> rangeQuery = new TableQuery<Stats>()
                .Where(TableQuery.CombineFilters(filter_to_part_row, TableOperators.And, filter_to_time_range))
                .Take(10);
            Stats[] results = null;
            try {
                results = store.stats.ExecuteQuery(rangeQuery).ToArray();
            } catch (Exception e) {
                Debug.WriteLine("[-] Error Retrieving stats: " + e.ToString());
            }
            return results;
        }
    }
}
