using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Utils {
    public struct IncrementalIdentifierGenerator {
        private ulong m_IncID;
        public string GetID(string prefix) {
            return $"{prefix}_{(m_IncID++):x}";
        }
        public void Reset() => m_IncID = 0;

    }
}
