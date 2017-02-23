using Microsoft.WindowsAzure.Storage.Queue;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace WorkerRole1 {
    public class WebCrawler {
        private readonly static Storage store = Storage.instance;
        public WorkerRole.STATES state = WorkerRole.STATES.IDLE;

        public WebCrawler() {
            ServicePointManager.DefaultConnectionLimit = 12;
            ServicePointManager.Expect100Continue = false;
        }

        public async Task<URL> Crawl(string url) {
            string[] urldata = await store.getURLData(url);
            bool haveNotCrawledYet = urldata == null || urldata[0] == null;
            if (url != null && haveNotCrawledYet) {
                if (WorkerRole.DEBUG) {
                    Debug.WriteLine("[*] URL to crawl: " + url);
                }
                WebResponse resp = await httpRequest(url);
                if (resp == null) return null; // if the request was bad (404 error or something)
                if (resp.ContentType.StartsWith("text/html")) {
                    this.state = WorkerRole.STATES.RUNNING;
                    StreamReader objReader = new StreamReader(resp.GetResponseStream());
                    string html = objReader.ReadToEnd();
                    return await parseHTML(url, html);
                } else if (resp.ContentType.StartsWith("text/xml") || resp.ContentType.StartsWith("application/xml")) {
                    this.state = WorkerRole.STATES.LOADING;
                    StreamReader objReader = new StreamReader(resp.GetResponseStream());
                    string xml = objReader.ReadToEnd();
                    await parseXML(xml);
                } else { // MIME type isn't html or xml... so we should ignore it.
                    Debug.WriteLine("[*] Ignoring " + url + " as it is not a desireable MIME type: " + resp.ContentType);
                }
            } else {
                if (WorkerRole.DEBUG) {
                    Debug.WriteLine("[-] Error: Crawl URL is `null` or it's been Crawled before.");
                }
            }
            return null;
        }

        public static bool validDate(string date) {
            try {
                DateTime date1 = DateTime.UtcNow;
                DateTime date2 = DateTime.Parse(date);
                int diff = (((date1.Year - date2.Year) * 12) + date1.Month - date2.Month);
                return diff <= 2;
            } catch {
                // there was an error in parsing... probably not a date, proper. it's probably good to go.
                // that is, the url probably was something like /sports/videos/..., and not /2017/02/16/...
                if (WorkerRole.DEBUG) {
                    Debug.WriteLine("[-] DATE PARSING ERROR: " + date);
                }
                return true;
            }

        }

        private static bool isRelativeURL(string url) {
            if (url.Length <= 1) return true;
            return (url[0] == '/') && (url[1] != '/');
        }

        public static string[] parseURL(string url) {
            url = url.Trim().ToLower();
            string[] result = new string[5];
            bool valid = false;
            Match match = Regex.Match(url, @"^(?:(https?):)?\/\/(.*?)(?:(\/.*?))?(?:\?(.*?))?(?:#(.*))?$");
            if (match.Success) {
                valid = true;
            } else if ((match = Regex.Match(url, @"^()()(?:(\/(?:[^/].*?)?))(?:\?(.*?))?(?:#(.*))?$")).Success) {
                valid = true;
            } else {
                if (WorkerRole.DEBUG) {
                    Debug.WriteLine("[-] parseURL ERROR -> match failure: " + url);
                }
                return null;
            }
            if (valid) {
                for (var i = 0; i < result.Length; i++) {
                    try {
                        result[i] = match.Groups[i + 1].Value;
                    } catch {
                        Debug.WriteLine("[-] Regex Match Error: " + url);
                        result[i] = "";
                    }
                }
            }
            return result;
        }

        public static string parseDomain(string[] urlComponents) {
            string[] domainComponents = urlComponents[1].Split('.');
            return String.Join(".", new string[] {
                domainComponents[domainComponents.Length - 2],
                domainComponents[domainComponents.Length - 1]
            });
        }

        public static bool checkIfSubStringInList(string toCheck, List<string> list) {
            foreach (string str in list) {
                if (toCheck.StartsWith(str)) {
                    return true;
                }
            }
            return false;
        }

        private async Task<bool> parseXML(string xml) {
            var list = XDocument.Parse(xml);
            XNamespace rootNamespace = list.Root.Name.Namespace;
            if (WorkerRole.DEBUG) {
                Debug.WriteLine("[*] sitemap count: " + list.Descendants(rootNamespace + "sitemap").Count());
                Debug.WriteLine("[*] url count: " + list.Descendants(rootNamespace + "url").Count());
            }
            if (list.Descendants(rootNamespace + "sitemap").Count() > 0) {
                var sitemaps = list.Descendants(rootNamespace + "sitemap");
                foreach (var sitemap in sitemaps) {
                    await xmlParseHelper(rootNamespace, sitemap, true);
                }
            } else if (list.Descendants(rootNamespace + "url").Count() > 0) {
                if (WorkerRole.DEBUG) {
                    Debug.WriteLine("[*] Namespace " + rootNamespace + " => " + rootNamespace.NamespaceName);
                }
                var earls = list.Descendants(rootNamespace + "url");
                foreach (var earl in earls) {
                    await xmlParseHelper(rootNamespace, earl, false);
                }
            }
            return true;
        }

        private async Task<bool> xmlParseHelper(XNamespace rootns, XElement node, bool sitemapIndex) {
            string lm = null;
            if (sitemapIndex) {
                lm = node.Element(rootns + "lastmod").Value;
            } else {
                foreach (var pd in node.Descendants()) {
                    if (pd.Name.LocalName == "publication_date") {
                        if (WorkerRole.DEBUG) {
                            Debug.WriteLine("[!] pd.Name.Namespace: " + pd.Name.Namespace + " <=rootNamespace=> " + rootns);
                        }
                        lm = pd.Value;
                        break;
                    }
                };
            }
            if (WorkerRole.DEBUG) {
                Debug.WriteLine("[*] lm: " + (lm == null ? "" : lm));
            }
            if (lm == "" || lm == null || validDate(lm)) {
                string earl = node.Element(rootns + "loc").Value;
                if (WorkerRole.DEBUG) {
                    Debug.WriteLine("[*] Attempting to add " + earl + " to Queue...");
                }
                bool added = await store.AddToQueue(earl, true);
                if (added) {
                    if (WorkerRole.DEBUG) {
                        Debug.WriteLine("[+] Added " + earl + " to Queue Successfully!");
                    }
                    return true;
                } else {
                    if (WorkerRole.DEBUG) {
                        Debug.WriteLine("[-] Failed to add " + earl + " to Queue due to robots restrictions.");
                    }
                }
            } else {
                if (WorkerRole.DEBUG) {
                    Debug.WriteLine("[-] Skipping " + node.Element(rootns + "loc").Value + " as it has an invalid date.");
                }
            }
            return false;
        }

        private async Task<URL> parseHTML(string url, string htmlString) {
            Match match = Regex.Match(htmlString, @"<title.*?>(.*?)<\/title>", RegexOptions.IgnoreCase);
            string title = null;
            if (match.Success) {
                title = match.Groups[1].Value;
            } else {
                Debug.WriteLine("[-] This page has no title: " + url);
            }
            MatchCollection links = Regex.Matches(htmlString, "<a .*?href=\"?(.*?)(?:\"| ).*? ?>", RegexOptions.IgnoreCase);
            if (links.Count > 1) {
                if (WorkerRole.DEBUG) {
                    Debug.WriteLine("[*] Pulled " + (links.Count - 1) + " links off of page");
                }
                Task.Run(() => getLinks(links, url)); // I think i want to specifically not await here...
            } else {
                Debug.WriteLine("[-] No links to grab on " + url);
            }

            return await store.addToURLTable(url, title);
        }

        private async Task getLinks(MatchCollection links, string url) {
            string[] urlComponents = parseURL(url);
            // partition based on domain (including subdomain) and have a hashset so we get unique urls
            Dictionary<string, List<HashSet<URL>>> batches = new Dictionary<string, List<HashSet<URL>>>();
            foreach (Match link in links) {
                string earl = link.Groups[1].Value;
                string[] earlComponents = parseURL(earl);
                if (earlComponents != null) {
                    if (earlComponents[1] == "") {
                        earlComponents[0] = urlComponents[0] + "://";
                        earlComponents[1] = urlComponents[1];
                        earl = String.Join("", earlComponents);
                    }
                    if (earlComponents[0] == "") {
                        earlComponents[0] = "http://";
                        earl = String.Join("", earlComponents);
                    }
                    if (WorkerRole.DEBUG) {
                        Debug.WriteLine("[*] link pulled: " + earl);
                    }
                    if (await store.getURLData(earl) == null) { // we don't have data on it
                        if (await store.AddToQueue(earl, false)) {
                            List<HashSet<URL>> tmp = null;
                            try {
                                if (!batches.TryGetValue(earlComponents[1], out tmp)) {
                                    tmp = batches[earlComponents[1]] = new List<HashSet<URL>>();
                                    tmp.Add(new HashSet<URL>());
                                }
                                var set = tmp[tmp.Count - 1];
                                if (set.Count >= 99) { // don't want to make new one everytime, just when latest one is full
                                    tmp.Add(new HashSet<URL>());
                                }
                                tmp[tmp.Count - 1].Add(new URL(earl, null));
                            } catch (Exception e) {
                                Debug.WriteLine("[-] Batch Uniqueness Preparation Error: " + e.ToString());
                            }
                            // NOTE: this null title indicates that the entry hasn't been crawled, but added to q
                            // this is so that i don't have to readd it to the queue.
                            //await store.addToURLTable(earl, null);
                        }
                    }
                }
            }
            foreach (var key in batches) {
                foreach (HashSet<URL> set in key.Value) {
                    await store.addToURLTableBatch(set);
                }
            }
        }

        public async Task<WebResponse> httpRequest(string url) {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Masqrawler v0.1";
            req.Proxy = null;
            try {
                WebResponse resp = await req.GetResponseAsync();
                return resp;
            } catch (Exception e) {
                Debug.WriteLine("[-] Error in httpRequest: {0} when attempting to access {1}.", e, url);
                WorkerRole.errors.Enqueue(new KeyValuePair<string, Exception>(url, e));
                return null;
            }
        }

        public async Task getRobots(string url) {
            string domain = parseDomain(parseURL(url));
            WorkerRole.robots.Add(domain, new List<string>());
            WebResponse resp = await httpRequest(url);
            if (resp != null) {
                StreamReader sr = new StreamReader(resp.GetResponseStream());
                string line = "";
                while ((line = sr.ReadLine()) != null) {
                    string[] keys = new string[] { "Sitemap:", "User-Agent:", "Allow:", "Disallow:" };
                    string value = null;
                    if (line.StartsWith(keys[0])) { // sitemap link
                        value = line.Substring(keys[0].Length).Trim();
                        // need to check if it's bleacherreport and nba then let through. or if you're anyone else, let through.
                        if (domain != "bleacherreport.com" || (domain == "bleacherreport.com" && value.Contains("nba"))) {
                           await store.earlQ.AddMessageAsync(new CloudQueueMessage(value));
                        }
                    } else if (line.StartsWith(keys[1])) { // user agent spec
                        value = line.Substring(keys[1].Length).Trim();
                    } else if (line.StartsWith(keys[2])) { // allow path
                        value = line.Substring(keys[2].Length).Trim();
                    } else if (line.StartsWith(keys[3])) { // disallow path
                        value = line.Substring(keys[3].Length).Trim();
                        WorkerRole.robots[domain].Add(value);
                    }
                }
            }
        }
    }
}
