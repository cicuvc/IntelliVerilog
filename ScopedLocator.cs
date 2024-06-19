using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core {
    public class ScopedLocator {
        [ThreadStatic]
        protected static ScopedLocator? m_ThreadLocalLocator;

        protected List<(Type type, object value)> m_OverrideStack = new();
        private struct LocatorRollback:IDisposable {
            private ScopedLocator m_Locator;
            private int m_Index;
            public LocatorRollback(ScopedLocator locator, int index) {
                m_Locator = locator;
                m_Index = index;
            }
            public void Dispose() {
                m_Locator.m_OverrideStack.RemoveRange(m_Index, m_Locator.m_OverrideStack.Count - m_Index);
            }
        }
        public static IDisposable RegisterValue<T>(T value) where T:class{
            m_ThreadLocalLocator ??= new();
            var index = m_ThreadLocalLocator.m_OverrideStack.Count;
            m_ThreadLocalLocator.m_OverrideStack.Add((typeof(T), value));
            return new LocatorRollback(m_ThreadLocalLocator, index);
        }
        public static T? GetService<T>() where T : class {
            var type = typeof(T);
            m_ThreadLocalLocator ??= new();
            var overrideStack = m_ThreadLocalLocator.m_OverrideStack;
            for (var i= overrideStack.Count - 1; i >= 0; i--) {
                if (overrideStack[i].type == type) {
                    return (T)overrideStack[i].value;
                }
            }
            return IntelliVerilogLocator.GetService<T>();
        }
    }
}
