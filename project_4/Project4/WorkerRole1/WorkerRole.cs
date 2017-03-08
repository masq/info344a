using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using System.Configuration;
using System.IO;

namespace WorkerRole1 {
    public class WorkerRole : RoleEntryPoint {
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly ManualResetEvent runCompleteEvent = new ManualResetEvent(false);

        private static readonly PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        private static readonly PerformanceCounter ramCounter = new PerformanceCounter("Memory", "Available MBytes");
        private static readonly Storage store = Storage.instance;
        private static STATES state = STATES.UNINIT;
        private static DateTime timer = DateTime.UtcNow;

        public static readonly bool DEBUG = false; // TODO
        public static int report_threshhold_in_seconds = 10; // 10 seconds

        public static List<WebCrawler> crawlers = new List<WebCrawler>();
        public static string[] domains { get; set; }
        public enum STATES : byte { INIT, LOADING, RUNNING, IDLE, PAUSED, STOPPED, UNINIT };
        public static int urlsCrawled = 0;
        public static int crawlerCount = 2;
        public static Queue<KeyValuePair<string, Exception>> errors = new Queue<KeyValuePair<string, Exception>>();
        public static Dictionary<string, string[]> urlComponentsCache = new Dictionary<string, string[]>();

        // domain => disallowed paths dict
        public static Dictionary<string, List<string>> robots;

        public override void Run() {
            Trace.TraceInformation("WorkerRole1 is running");

            try {
                this.RunAsync(this.cancellationTokenSource.Token).Wait();
            } finally {
                this.runCompleteEvent.Set();
            }
        }

        public override bool OnStart() {
            // Set the maximum number of concurrent connections
            ServicePointManager.DefaultConnectionLimit = 125;
            ServicePointManager.Expect100Continue = false;

            // For information on handling configuration changes
            // see the MSDN topic at https://go.microsoft.com/fwlink/?LinkId=166357.

            bool result = base.OnStart();

            //List<WebCrawler> wcl = new List<WebCrawler>();
            for (var i = 0; i < crawlerCount; i++) {
                crawlers.Add(new WebCrawler());
            }
            domains = new string[] { "cnn.com", "bleacherreport.com" };
            RoleEnvironment.TraceSource.Switch.Level = SourceLevels.Information; // Try to cut down on the noise...


            Trace.TraceInformation("WorkerRole1 has been started");

            return result;
        }

        public override void OnStop() {
            Trace.TraceInformation("WorkerRole1 is stopping");

            this.cancellationTokenSource.Cancel();
            this.runCompleteEvent.WaitOne();

            base.OnStop();

            Trace.TraceInformation("WorkerRole1 has stopped");
        }

        private async Task RunAsync(CancellationToken cancellationToken) {
            while (!cancellationToken.IsCancellationRequested) {
                try {
                    if (store.commandq != null) {
                        // check if the admin console has sent us any messages to process.
                        CloudQueueMessage message = await store.commandq.GetMessageAsync();
                        if (message != null) {
                            string[] components = message.AsString.Split('_');
                            string cmd = components[0];
                            await store.reportToWebRole("[*] Got " + cmd + " Command while in " + state + " state...");
                            if (cmd == "start") state = STATES.INIT;
                            else if (cmd == "pause") state = STATES.PAUSED;
                            else if (cmd == "stop") state = STATES.STOPPED;
                            else if (cmd == "resume") state = STATES.RUNNING;
                            else if (cmd == "getState") {
                                await store.reportToWebRole("[#] " + (byte) (getState() == STATES.UNINIT ? STATES.INIT : getState())); // TODO: eww gross
                            } else if (cmd == "add") {
                                crawlers.Add(new WebCrawler());
                            } else if (cmd == "delete") {
                                int num = int.Parse(components[1]);
                                crawlers[num].state = STATES.STOPPED; // TODO
                            } else {
                                Debug.WriteLine("[!] Unknown Command Issued: " + cmd);
                            }
                            await store.reportToWebRole("[+] Processed " + cmd + " Command! Now in " + state + " state");
                            // This must come after using the message
                            await store.commandq.DeleteMessageAsync(message);
                        }

                        // report stats every ten seconds. On success, reset timer to 0.
                        // TODO: get rid of this timer thing, use datetime.utcnow and a difference of ten or more.
                        DateTime now = DateTime.UtcNow;
                        DateTime check = timer.AddSeconds(report_threshhold_in_seconds);
                        if (now >= check && !(state == STATES.IDLE || state == STATES.UNINIT)) {
                            Debug.WriteLine("[*] Reporting... " + now + " - "
                                + check + " = "
                                + (now - check).Seconds);
                            await store.reportStats();
                            await store.reportErrors();
                            timer = now;
                        }

                        switch (state) {
                            case STATES.INIT:
                                if (await store.recreateTables() && await init()) {
                                    Debug.WriteLine("[*] Switching from STATES.INIT to STATES.IDLE");
                                    await store.reportToWebRole("[!] Switching from STATES.INIT to STATES.IDLE");

                                    state = STATES.IDLE; // TODO
                                    //state = STATES.RUNNING
                                }
                                break;
                            case STATES.STOPPED:
                                await store.reportToWebRole("[*] Attempting to stop and clear everything...");
                                state = STATES.IDLE;
                                bool success = await store.clearEverything();
                                if (success) {
                                    Debug.WriteLine("[!] Everything has been stopped!");
                                    await store.reportToWebRole("[+] Everything has been stopped successfully!");
                                }
                                await Task.Delay(1 * 60 * 1000); // wait for 1 minutes for everything to clear...
                                goto case STATES.IDLE;
                            case STATES.RUNNING:
                                List<Task> crawls = new List<Task>();
                                foreach (WebCrawler wc in crawlers) {
                                    crawls.Add(crawl(wc)); // don't need await here
                                }
                                goto case STATES.IDLE; // C# doesn't allow for case fallthrough >_<
                            case STATES.IDLE:
                            case STATES.PAUSED:
                            default:
                                if (state == STATES.IDLE || state == STATES.PAUSED) {
                                    foreach (WebCrawler wc in crawlers) {
                                        wc.state = STATES.IDLE;
                                    }
                                }
                                await Task.Delay(1000);
                                break;
                        }
                    } else {
                        await Task.Delay(1000);
                    }
                } catch (Exception e) {
                    Debug.WriteLine("[-] Error - SOMETHING TERRIBLE HAS HAPPENED: " + e.ToString());
                    await Task.Delay(1000);
                }
            }
        }

        private async Task crawl(WebCrawler wc) {
            CloudQueueMessage msg = await store.earlQ.GetMessageAsync();
            if (msg != null) {
                URL recent = await wc.Crawl(msg.AsString);
                if (recent != null) {
                    store.recents.Enqueue(recent);
                    urlComponentsCache.Remove(recent.url);
                    urlsCrawled++;
                    if (store.recents.Count > 10) {
                        store.recents.Dequeue();
                    }
                }
                try {
                    await store.earlQ.DeleteMessageAsync(msg);
                } catch {
                    Debug.WriteLine("[-] 404, message can't be deleted because it doesn't exist???");
                }
            } else {
                wc.state = STATES.IDLE;
            }
        }

        private static async Task<bool> init() {
            robots = new Dictionary<string, List<string>>();
            Debug.WriteLine("[*] Start Command Issued. Starting... ");
            await store.reportToWebRole("[!] Start Command Issued. Starting...");
            int i = 0;
            foreach (string domain in domains) {
                Debug.WriteLine("[*] Getting robots.txt for " + domain + "...");
                await store.reportToWebRole("[!] Getting robots.txt for " + domain + "...");
                await crawlers[i % crawlers.Count].getRobots("http://" + domain + "/robots.txt");
                i++;
            }
            Debug.WriteLine("[+] Crawlers Initialized");
            await store.reportToWebRole("[!] Crawlers Initialized");
            return true;
        }



        public static STATES getState() {
            return state;
        }

        public static STATES[] getCrawlerStates() {
            STATES[] result = new STATES[crawlers.Count];
            int i = 0;
            foreach (WebCrawler wc in crawlers) {
                result[i++] = wc.state;
            }
            return result;
        }

        public static string getCurrentCpuUsage() {
            return cpuCounter.NextValue() + "%";
        }

        public static string getAvailableRAM() {
            return ramCounter.NextValue() + "MB";
        }

        public static async Task<int> getQueueSize() {
            await store.earlQ.FetchAttributesAsync();
            int? size = store.earlQ.ApproximateMessageCount;
            if (size != null && size.HasValue) {
                return size.Value;
            } else {
                return -1;
            }
        }
    }
}
