using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Web;

namespace WebRole1 {
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
                Debug.WriteLine("[-] ERROR: INVALID CHARACTER PASSED INTO TRIE CONSTRUCTOR: " + (int) c);
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
                        tmp.AddRange(this._Children[i].GetSuggestions("", built + (this._IsRoot ? "" : "" + this._Key)));
                        if (tmp.Count >= 10) {
                            return tmp.GetRange(0, 10);
                        }
                    }
                }
                // if i run through everything and haven't gotten an answer of ten things... I don't have a suggestion
                return null;
            }
            int position = prefix[0] == ' ' ? CHARS - 1 : (char)prefix[0] - 'a';
            try {
                return this._Children[position].GetSuggestions(prefix.Substring(1), built + (this._IsRoot ? "" : "" + this._Key));
            } catch(Exception e) {
                return new List<string>();
            }
        }
    }
}