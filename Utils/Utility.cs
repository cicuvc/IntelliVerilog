using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Utils {
    public static class Utility {
        private static Random m_Random = new();
        private readonly static char[] m_HexDigits = new char[] {
            '0','1','2','3','4','5','6','7','8','9','A','B','C','D','E'
        };
        public static string GetRandomStringHex(int length) {
            var sb = new StringBuilder();
            while ((length--) > 0) sb.Append(m_HexDigits[m_Random.Next(m_HexDigits.Length)]);
            return sb.ToString();
        }
        public static string GetArraySignature(object[] array) {
            var sb = new StringBuilder();
            sb.Append($"{array.Length:X}");
            foreach(var i in array) {
                if(i is null) {
                    sb.Append("_NIL");
                } else {
                    sb.Append($"_{i.GetHashCode():X02}");
                }
                
            }
            return sb.ToString();
        }
    }
}
