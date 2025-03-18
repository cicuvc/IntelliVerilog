using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core {
    public class ScopedLocator {
        [ThreadStatic]
        protected static ScopedLocator? m_ThreadLocalLocator;

        protected interface IScopedLocatorStorage {
            void PopValue();
        }
        protected static class ScopedLocatorStorage<T> where T : class {
            [ThreadStatic]
            private static ScopedOverrideStack m_Stack = new();
            public class ScopedOverrideStack: Stack<T>, IScopedLocatorStorage {
                public void PopValue() => Pop();
            }
            public static ScopedOverrideStack Stack => m_Stack;
        }

        protected Stack<IScopedLocatorStorage> m_OverrideStack = new();
        private struct LocatorRollback:IDisposable {
            private ScopedLocator m_Locator;
            private int m_Index;
            public LocatorRollback(ScopedLocator locator, int index) {
                m_Locator = locator;
                m_Index = index;
            }
            public void Dispose() {
                var count = m_Locator.m_OverrideStack.Count - m_Index;
                for(var i = 0; i < count; i++) {
                    m_Locator.m_OverrideStack.Pop().PopValue();
                }
            }
        }
        public static IDisposable RegisterValue<T>(T value) where T:class{
            m_ThreadLocalLocator ??= new();
            var index = m_ThreadLocalLocator.m_OverrideStack.Count;

            ScopedLocatorStorage<T>.Stack.Push(value);

            m_ThreadLocalLocator.m_OverrideStack.Push(ScopedLocatorStorage<T>.Stack);

            return new LocatorRollback(m_ThreadLocalLocator, index);
        }
        public static T GetServiceNonNull<T>() where T : class {
            return GetService<T>() ?? throw new NullReferenceException("Requested service is null");
        }
        public static T? GetService<T>() where T : class {
            var type = typeof(T);
            m_ThreadLocalLocator ??= new();

            if(ScopedLocatorStorage<T>.Stack.TryPeek(out var result)) {
                return result;
            }
            return IntelliVerilogLocator.GetService<T>();
        }
    }
}
