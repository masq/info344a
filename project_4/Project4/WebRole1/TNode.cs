using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebRole1 {
    interface TNode {
        // adds a word to the node
        TNode AddWord(string word);
        // returns string[] of suggestions for completion to prefix
        List<string> GetSuggestions(string prefix, string built);
    }
}
