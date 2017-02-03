using System.Collections.Generic;




interface TNode {
    // adds a word to the node
    TNode AddWord(string word);
    // returns total number of children in node (recursively)
    int GetRChildren();
    // returns string[] of suggestions for completion to prefix
    string[] GetSuggestions(string prefix);
}

class ListNode : TNode {
    public int ChildCount { private set; get; }
    private char _Key { set; get; }
    private List<string> _children { set; get; }

    public static readonly byte SUGGESTION_MAX = 10;
    public static readonly byte THRESHHOLD = 50;

    public ListNode(char c) {
        this._Key = c;
    }

    public int GetRChildren() {
        return this.ChildCount;
    }

    public TNode AddWord(string suffix) {
        // when children under 50, suffixs should be represented in a list.
        // when children > 50, suffix list should be broken out into a new trie.
        if (suffix == null || suffix == "") {
            return this; // TODO: or should return null?? yes, because it doesn't change!! :D
        }
        // add child
        this._children.Add(suffix);

        this.ChildCount++;
        
        if (this.ChildCount > ListNode.THRESHHOLD) {
            return new Trie(this._Key, this._children);
        } else {
            return this;
        }
    }

    public string[] GetSuggestions(string prefix) {
        if (prefix == null || prefix == "") {
            return null;
        }
        return this._children.GetRange(0, SUGGESTION_MAX).ToArray();
    }
}

class Trie : TNode {
    private TNode[] _children;
    public int ChildCount { private set; get; }
    private char _key;

    public static readonly byte CHARS = 27;

    // initialized with null byte... only called for an overarching Trie or 'Public' Trie
    public Trie() : this('\x00', null) { }
    // made but without any strings to pass in
    public Trie(char c) : this(c, null) { }
    // made with a character, and with a list of strings to make as children already
    public Trie(char c, List<string> keys) {
        if (c < 'a' || c > 'z' || c != ' ') {
            // THIS SHOULDN'T HAPPEN :(
        }
        this._key = c;
        this.ChildCount = 0;
        this._children = new TNode[27];
        if (keys != null) {
            foreach (string s in keys) {
                AddWord(s);
            }
        }
    }

    public int GetRChildren() {
        return this.ChildCount;
    }

    public TNode AddWord(string word) {
        if (word == null || word == "") {
            return this; // TODO: or should return null?? yes, because it doesn't change!! :D
        }
        TNode slot = this._children[word[0]];
        if (slot == null) {
            slot = new ListNode(word[0]);
        }
        // this will change the slot into either a Trie or a ListNode depending...
        this._children[word[0]] = slot.AddWord(word.Substring(1));
        this.ChildCount++;
        return this; // TODO: should I return this or the ListNode that word got added to?
                    //this, because it doesn't change!
    }

    public string[] GetSuggestions(string prefix) {
        if (prefix == null || prefix == "") {
            return null;
        }
        return this._children[prefix[0]].GetSuggestions(prefix.Substring(1));
    }
}


//    // END OF TRIE METHODS
//    // NODES:

//    interface INodeCollection {
//        bool TryGetNode(char key, out Trie node);
//        INodeCollection Add(char key, Trie node);
//        IEnumerable<Trie> GetNodes();
//    }

//    class SingleNode : INodeCollection {
//        internal readonly char _key;
//        internal readonly Trie _trie;

//        public SingleNode(char key, Trie trie) {
//            this._key = key;
//            this._trie = trie;
//        }

//        public Add(char key, Trie node) {

//        }

//        // Add returns a SmallNodeCollection.
//    }

//    class SmallNodeCollection : INodeCollection {
//        const int MaximumSize = 8; // ?

//        internal readonly List<KeyValuePair<char, Trie>> _nodes;

//        public SmallNodeCollection(SingleNode node, char key, Trie trie) { /*...*/ }

//        // Add adds to the list and returns the current instance until MaximumSize,
//        // after which point it returns a LargeNodeCollection.
//    }

//    class LargeNodeCollection : INodeCollection {
//        private readonly Dictionary<char, Trie> _nodes;

//        public LargeNodeCollection(SmallNodeCollection nodes, char key, Trie trie) { /*...*/ }

//        // Add adds to the dictionary and returns the current instance.
//    }
//}

