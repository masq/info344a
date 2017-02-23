using Microsoft.WindowsAzure.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WorkerRole1 {
    public class Storage {

        public CloudQueue commandq { get; private set; }
        public CloudQueue earlQ { get; private set; }
        public CloudTable stats { get; private set; }
        public CloudTable urldata { get; private set; }

        public Queue<URL> recents = new Queue<URL>();

        // try to make this a singleton
        public readonly static Storage instance = new Storage();
        public Storage() {
            if (instance == null) {
                Debug.WriteLine("[*] Initializing Storage...");

                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                    ConfigurationManager.AppSettings["StorageConnectionString"]);

                // create q clients
                CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
                this.commandq = queueClient.GetQueueReference("commands");
                this.commandq.CreateIfNotExists();
                this.commandq.Clear();

                this.earlQ = queueClient.GetQueueReference("urls");
                this.earlQ.CreateIfNotExists();
                this.earlQ.Clear();

                // create t client
                CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
                this.stats = tableClient.GetTableReference("stats");
                this.stats.CreateIfNotExists();
                this.urldata = tableClient.GetTableReference("urldata");
                this.urldata.CreateIfNotExists();

                Debug.WriteLine("[+] Storage Initialized.");
            }
        }

        public async Task<bool> clearEverything() {
            bool status = false;
            status = status || await clearQueues();
            status = status && await deleteTables();
            return status;
        }

        public async Task<bool> clearQueues() {
            await this.commandq.ClearAsync();
            await this.earlQ.ClearAsync();
            return true;
        }

        public async Task<bool> deleteTables() {
            await this.stats.DeleteIfExistsAsync();
            await this.urldata.DeleteIfExistsAsync();
            return true;
        }

        public Storage getInstance() {
            return instance;
        }

        public async Task<bool> AddToQueue(string loc, bool xml) {
            string[] urlComponents = WebCrawler.parseURL(loc);
            string domain = WebCrawler.parseDomain(urlComponents);
            // some of the urls have a date embedded in them... lets get rid of the ones that are too old
            if (xml) {
                Match match = Regex.Match(urlComponents[2], @"((?:\/(?:\d{4}|\d{2})){1,3})", RegexOptions.IgnoreCase);
                if (match.Success) {
                    string[] datePath = match.Groups[1].Value.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    return WebCrawler.validDate(String.Join("-", datePath));
                }
            }
            if (WorkerRole.domains.Contains(domain) && !WebCrawler.checkIfSubStringInList(urlComponents[2], WorkerRole.robots[domain])) {
                await earlQ.AddMessageAsync(new CloudQueueMessage(loc));
                return true;
            }
            return false;
        }

        public async Task<string[]> getURLData(string url) {
            if (url == null) return null;
            string domain = WebCrawler.parseURL(url)[1];
            string hash = URL.sha256(url);
            if (WorkerRole.DEBUG) {
                Debug.WriteLine("[*] Getting data for " + url + " => " + domain + " | " + hash + "...");
            }
            TableOperation get = TableOperation.Retrieve<URL>(domain, hash);
            TableResult result = await urldata.ExecuteAsync(get);
            if (result.Result != null) {
                if (WorkerRole.DEBUG) {
                    Debug.WriteLine("[+] Got Data for " + url);
                }
                URL earl = (URL)result.Result;
                return new string[] { earl.title, earl.Timestamp.ToString("o"), earl.url };
            } else {
                if (WorkerRole.DEBUG) {
                    Debug.WriteLine("[-] No Data for " + url + ".");
                }
                return null;
            }
        }

        public string[] getURLDataSync(string url) {
            if (url == null) return null;
            string domain = WebCrawler.parseURL(url)[1];
            string hash = URL.sha256(url);
            if (WorkerRole.DEBUG) {
                Debug.WriteLine("[*] Getting data for " + url + " => " + domain + " | " + hash + "...");
            }
            TableOperation get = TableOperation.Retrieve<URL>(domain, hash);
            TableResult result = urldata.Execute(get);
            if (result.Result != null) {
                if (WorkerRole.DEBUG) {
                    Debug.WriteLine("[+] Got Data for " + url);
                }
                URL earl = (URL)result.Result;
                return new string[] { earl.title, earl.Timestamp.ToString("o"), earl.url };
            } else {
                if (WorkerRole.DEBUG) {
                    Debug.WriteLine("[-] No Data for " + url + ".");
                }
                return null;
            }
        }

        public async Task<URL> addToURLTable(string url, string title) {
            if (url != null) {
                if (WorkerRole.DEBUG) {
                    Debug.WriteLine("[*] Adding " + url + " => " + title + " to table...");
                }
                URL earl = new URL(url, title);
                TableOperation insert = TableOperation.InsertOrReplace(earl);
                await urldata.ExecuteAsync(insert);
                if (WorkerRole.DEBUG) {
                    Debug.WriteLine("[+] Added!");
                }
                return earl;
            } else {
                return null;
            }
        }

        public async Task addToURLTableBatch(HashSet<URL> entities) {
            if (entities != null) {
                int id = new Random().Next(0, int.MaxValue);
                if (WorkerRole.DEBUG) {
                    Debug.WriteLine("[*] Adding a batch of " + entities.Count + " URLs to table...");
                }
                List<TableBatchOperation> ops = new List<TableBatchOperation>();
                ops.Add(new TableBatchOperation());
                int i = 0;
                try {
                    foreach (URL entity in entities) {
                        if (WorkerRole.DEBUG) {
                            Debug.WriteLine("[*] " + id + "-" + i++ + ": " + entity + " => " + entity.url + "|||" + entity.title + "|||" + entity.PartitionKey + "T" + entity.RowKey);
                        }
                        if (ops[ops.Count - 1].Count >= 99) {
                            ops.Add(new TableBatchOperation());
                        }
                        ops[ops.Count - 1].InsertOrMerge(entity);
                    }
                } catch (Exception e) {
                    Debug.WriteLine("[-] Batch Preparation Error: " + e.ToString());
                }
                try {
                    foreach (var op in ops) {
                        await urldata.ExecuteBatchAsync(op);
                    }
                } catch(Exception e) {
                    Debug.WriteLine("[-] batch error: " + e.ToString() + " => There were " + entities.Count + " many entities");
                    i = 0;
                    foreach (URL entity in entities) {
                        Debug.WriteLine("[*] " + id + "-" + i++ + ": " + entity + " => " + entity.url + "|||" + entity.title + "|||" + entity.PartitionKey + "T" + entity.RowKey);
                    }
                }
                if (WorkerRole.DEBUG) {
                    Debug.WriteLine("[+] Added!");
                }
            } else {
                if (WorkerRole.DEBUG) {
                    Debug.WriteLine("[-] Error: entities batch hashset was `null`.");
                }
            }
        }

        public async Task<bool> reportStats() {
            //Debug.WriteLine("[*] Reporting statistics...");
            URL[] arr = recents.ToArray();
            int urlSize = WorkerRole.urlsCrawled;
            int qSize = await WorkerRole.getQueueSize();
            string ram = WorkerRole.getAvailableRAM();
            string cpu = WorkerRole.getCurrentCpuUsage();
            WorkerRole.STATES[] states = WorkerRole.getCrawlerStates();
            KeyValuePair<string, Exception>[] errors = WorkerRole.errors.ToArray();
            WorkerRole.errors.Clear();
            Stats inc = new Stats(arr, urlSize, qSize, cpu, ram, states, errors);
            TableOperation insert = TableOperation.Insert(inc);
            await stats.ExecuteAsync(insert);
            //Debug.WriteLine("[+] Stats updated");
            return true;
        }
    }
}
