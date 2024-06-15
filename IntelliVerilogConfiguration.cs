using MemoryPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core {
    
    public class IntelliVerilogConfiguration {
        protected Dictionary<string, object> m_Data = new();
        public T GetConfiguration<T>() where T:class, IMemoryPackable<T>, new(){
            var key = $"k{typeof(T).Name}";
            if (!m_Data.ContainsKey(key)) {
                m_Data.Add(key, new T());
            }
            return (T)m_Data[key];
        }
        public void Dump() {
            
        }
    }
}
