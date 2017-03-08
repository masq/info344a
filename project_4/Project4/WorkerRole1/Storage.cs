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
        public CloudQueue reporting { get; private set; }
        public CloudTable stats { get; private set; }
        public CloudTable urldata { get; private set; }
        public CloudTable errors { get; private set; }

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

                this.reporting = queueClient.GetQueueReference("reporting");
                this.reporting.CreateIfNotExists();
                this.reporting.Clear();

                // create t client
                CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
                this.stats = tableClient.GetTableReference("stats");
                this.stats.CreateIfNotExists();
                this.errors = tableClient.GetTableReference("errors");
                this.errors.CreateIfNotExists();
                this.urldata = tableClient.GetTableReference("urldata4");
                this.urldata.CreateIfNotExists();

                Debug.WriteLine("[+] Storage Initialized.");
                reportToWebRole("[+] Storage Initialized.");
            }
        }

        public async Task<bool> recreateTables() {
            try {
                await this.stats.CreateIfNotExistsAsync();
                await this.errors.CreateIfNotExistsAsync();
                await this.urldata.CreateIfNotExistsAsync();
                return true;
            } catch (Exception e) {
                Debug.WriteLine("[-] Error in recreateTables: " + e.ToString());
                await reportToWebRole("[-] Error in recreateTables: " + e.ToString());
                return false;
            }
        }

        public async Task<bool> clearEverything() {
            bool status = true;
            try {
                status = status && await clearQueues();
                status = status && await deleteTables();
            } catch (Exception e) {
                Debug.WriteLine("[-] Error in clearEverything: " + e.ToString());
                await reportToWebRole("[-] Error in clearEverything: " + e.ToString());
                status = false;
            }
            return status;
        }

        public async Task<bool> clearQueues() {
            //await this.commandq.ClearAsync();
            await this.earlQ.ClearAsync();
            //await this.reporting.ClearAsync();
            return true;
        }

        public async Task<bool> deleteTables() {
            await this.stats.DeleteIfExistsAsync();
            await this.urldata.DeleteIfExistsAsync();
            await this.errors.DeleteIfExistsAsync();
            return true;
        }

        public Storage getInstance() {
            return instance;
        }

        public async Task<bool> reportToWebRole(string msg) {
            try {
                await this.reporting.AddMessageAsync(new CloudQueueMessage(msg));
                return true;
            } catch (Exception e) {
                Debug.WriteLine("[-] Error in reportToWebRole: " + e.ToString());
                // pointless to call self with error if i can't run properly...
                return false;
            }
        }

        public List<string> getReports(int amt) {
            var messages = this.reporting.GetMessages(amt);
            List<string> msgs = new List<string>();
            foreach (var message in messages) {
                msgs.Add(message.AsString);
                this.reporting.DeleteMessageAsync(message);
            }
            return msgs;
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
            try {
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
            } catch (Exception e) {
                Debug.WriteLine("[-] Error in getURLData: " + e.ToString() + " => " + url);
                await reportToWebRole("[-] Error in getURLData: " + e.ToString() + " => " + url);
                return null;
            }
        }

        public List<URL> search(string word) {
            TableQuery<URL> rangeQuery = new TableQuery<URL>()
                .Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, word));
            try {
                return this.urldata.ExecuteQuery(rangeQuery).ToList();
            } catch (Exception e) {
                Debug.WriteLine("[-] Error in Storage.search: " + e.ToString() + " => " + word);
                reportToWebRole("[-] Error in Storage.search: " + e.ToString() + " => " + word);
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
            try {
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
            } catch (Exception e) {
                Debug.WriteLine("[-] Error in getURLData: " + e.ToString() + " => " + url);
                reportToWebRole("[-] Error in getURLData: " + e.ToString() + " => " + url);
                return null;
            }
        }

        public async Task<URL> addToURLTable(string url, string title, string word) {
            if (url != null) {
                if (WorkerRole.DEBUG) {
                    Debug.WriteLine("[*] Adding " + url + " => " + title + " to table...");
                }
                URL earl = (word != null? new URL(url, title, word) : new URL(url, title));
                TableOperation insert = TableOperation.InsertOrReplace(earl);
                try {
                    await urldata.ExecuteAsync(insert);
                    if (WorkerRole.DEBUG) {
                        Debug.WriteLine("[+] Added!");
                    }
                    return earl;
                } catch (Exception e) {
                    Debug.WriteLine("[-] Error in addToURLTable: " + e.ToString() + " => " + url);
                    await reportToWebRole("[-] Error in addToURLTable: " + e.ToString() + " => " + url);
                    return null;
                }
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
                    await reportToWebRole("[-] Batch Preparation Error: " + e.ToString());
                }
                try {
                    foreach (var op in ops) {
                        await urldata.ExecuteBatchAsync(op);
                    }
                } catch (Exception e) {
                    Debug.WriteLine("[-] batch error: " + e.ToString() + " => There were " + entities.Count + " many entities");
                    await reportToWebRole("[-] batch error: " + e.ToString() + " => There were " + entities.Count + " many entities");
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
            Stats inc = new Stats(arr, urlSize, qSize, cpu, ram, states);
            TableOperation insert = TableOperation.Insert(inc);
            try {
                await this.stats.ExecuteAsync(insert);
            } catch (Exception e) {
                Debug.WriteLine("[-] Error in reporting Stats: " + e.ToString() + " => " + inc);
                await reportToWebRole("[-] Error in reporting Stats: " + e.ToString() + " => " + inc);
            }
            //Debug.WriteLine("[+] Stats updated");
            return true;
        }

        public async Task<bool> reportErrors() {
            //Debug.WriteLine("[*] Reporting Errors...");
            KeyValuePair<string, Exception>[] errors = WorkerRole.errors.ToArray();
            if (errors.Length > 0) {
                Errors err = new Errors(errors);
                TableOperation insert_errors = TableOperation.Insert(err);
                try {
                    TableResult result = await this.errors.ExecuteAsync(insert_errors);
                    if (result != null) {
                        Debug.WriteLine("[+] Errors updated");
                        WorkerRole.errors.Clear();
                    } else {
                        Debug.WriteLine("[-] Errors not updated :(");
                        return false;
                    }
                } catch (Exception e) {
                    Debug.WriteLine("[-] Error in reporting Errors: " + e.ToString() + " => " + errors);
                    await reportToWebRole("[-] Error in reporting Errors: " + e.ToString() + " => " + errors);
                    return false;
                }
            }
            return true;
        }
    }
}
