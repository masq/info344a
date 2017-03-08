using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebRole1 {
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

        // TODO: fix when the suggestion is returning early with 10 things and could binary search for a better result
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
}