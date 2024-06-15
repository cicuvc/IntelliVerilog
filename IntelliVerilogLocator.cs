using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core {
    public static class IntelliVerilogLocator {
        private static Dictionary<Type, Func<object>> m_Services = new();
        public static T? GetService<T>() where T:class {
            m_Services.TryGetValue(typeof(T), out var value);
            return (T?)value?.Invoke();
        }
        public static void RegisterService<T>(Func<T> lazyFunction) where T : class {
            m_Services.Add(typeof(T), ()=>lazyFunction());
        }
        public static void RegisterService<T>(T lazyFunction) where T : class {
            m_Services.Add(typeof(T), () => lazyFunction);
        }
    }
}
