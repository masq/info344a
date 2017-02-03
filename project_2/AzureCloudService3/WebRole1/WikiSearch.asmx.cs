using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Web;
using System.Web.Services;
using Microsoft.WindowsAzure.Storage;
using System.Configuration;
using Microsoft.WindowsAzure.Storage.Blob;
using System.IO;
using System.Web.Script.Services;

namespace WebRole1 {
    /// <summary>
    /// Summary description for WikiSearch
    /// </summary>
    [WebService(Namespace = "http://tempuri.org/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [System.ComponentModel.ToolboxItem(false)]
    // To allow this Web Service to be called from script, using ASP.NET AJAX, uncomment the following line. 
    [System.Web.Script.Services.ScriptService]
    public class WikiSearch : System.Web.Services.WebService {

        private static PerformanceCounter _PC = new PerformanceCounter("Memory", "Available MBytes");
        private static Trie _Trey = null;

        // TODO: when going to prod, flip to false
        private static bool _Debug = false;

        [WebMethod]
        public float MemTest() {
            return _PC.NextValue();
        }

        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public string[] Query(string word) {
            // if something goes wrong... just rebuild trie when user queries
            if (_Trey == null) {
                bool complete = buildTrie();

                if (_Debug) {
                    Debug.WriteLine("[+] TRIE STATUS: " + complete);
                }
            }


            List<string> results = _Trey.GetSuggestions(word.ToLower().TrimStart(), "");
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
                    if (_Debug) {
                        if (prev != tmp[0]) {
                            prev = tmp[0];
                            Debug.WriteLine("[*] Adding words starting with '" + (""+tmp[0]).ToUpper() + "' to TRIE");
                        }
                        //if (prev > 'b') {
                        //    Debug.WriteLine("[!] Breaking at '" + prev + "' for debugging purposes!");
                        //    break;
                        //}
                    }
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

    interface TNode {
        // adds a word to the node
        TNode AddWord(string word);
        // returns string[] of suggestions for completion to prefix
        List<string> GetSuggestions(string prefix, string built);
    }

    class ListNode : TNode {
        private int _ChildCount;
        private char _Key;
        private List<string> _Children;

        public static readonly byte SUGGESTION_MAX = 10;
        public static readonly byte THRESHHOLD = 50;

        public ListNode(char c) {
            this._Key = c;
            this._Children = new List<string>();
            this._ChildCount = 0;
        }

        public TNode AddWord(string suffix) {
            // when children under THRESHHOLD, suffixs should be represented in a list.
            // when children > THRESHHOLD, suffix list should be broken out into a new trie.
            if (suffix == null || suffix == "") {
                return this;
            }
            // add child
            this._Children.Add(suffix);

            this._ChildCount++;

            if (this._ChildCount > ListNode.THRESHHOLD) {
                return new Trie(this._Key, this._Children);
            } else {
                return this;
            }
        }

        public List<string> GetSuggestions(string prefix, string built) {
            if (prefix == null) {
                return null;
            }
            List<string> tmp = this._Children.GetRange(0, Math.Min(this._Children.Count, SUGGESTION_MAX));
            for (byte i = 0; i < tmp.Count; i++) {
                tmp[i] = built + this._Key + tmp[i];
            }
            return tmp;
        }
    }

    class Trie : TNode {
        private TNode[] _Children;
        private int _ChildCount;
        private char _Key;
        private bool _IsRoot;

        public static readonly byte CHARS = 27;

        // made as the root node
        public Trie() : this(' ', null) {
            this._IsRoot = true;
        }
        // made but without any strings to pass in
        public Trie(char c) : this(c, null) { }
        // made with a character, and with a list of strings to make as children already
        public Trie(char c, List<string> keys) {
            if (c != ' ' && (c < 'a' || c > 'z')) {
                // THIS SHOULDN'T HAPPEN :(
                Debug.WriteLine("[-] ERROR: INVALID CHARACTER PASSED INTO TRIE CONSTRUCTOR: " + (int)c);
            }
            this._Key = c;
            this._IsRoot = false;
            this._ChildCount = 0;
            this._Children = new TNode[CHARS];
            if (keys != null) {
                foreach (string s in keys) {
                    AddWord(s);
                }
            }
        }

        public TNode AddWord(string word) {
            if (word == null || word == "") {
                return this; 
            }
            int position = word[0] == ' '? CHARS - 1 : (char)word[0] - 'a';
            TNode slot = this._Children[position];
            if (slot == null) {
                slot = new ListNode(word[0]);
            }
            // this will change the slot into either a Trie or a ListNode depending...
            this._Children[position] = slot.AddWord(word.Substring(1));
            this._ChildCount++;
            return this;
        }

        public List<string> GetSuggestions(string prefix, string built) {
            if (prefix == null) {
                return null;
            }
            if (prefix == "") {  // ran out of characters to walk down the Trie with... time for DFS!!
                List<string> tmp = new List<string>();
                for (byte i = 0; i < this._Children.Length; i++) {
                    if (this._Children[i] != null) { // skip nulls...
                        tmp.AddRange(this._Children[i].GetSuggestions("", built + (this._IsRoot ? "" : ""+this._Key)));
                        if (tmp.Count >= 10) {
                            return tmp.GetRange(0, 10);
                        }
                    }
                }
                // if i run through everything and haven't gotten an answer of ten things... I don't have a suggestion
                return null;
            }
            int position = prefix[0] == ' ' ? CHARS - 1 : (char)prefix[0] - 'a';
            return this._Children[position].GetSuggestions(prefix.Substring(1), built + (this._IsRoot? "" : ""+this._Key));
        }
    }
}
