using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core {
    public static class IntelliVerilogLocator {
        private static class LocatorImpl<T> {
            public static Lazy<T> Service { get; set; }
        }

        public static T? GetService<T>() where T:class {
            var service = LocatorImpl<T>.Service;
            if(service.IsNil) return default;
            return service.Value;
        }
        public static T GetServiceNonNull<T>() where T : class {
            return GetService<T>() ?? throw new NullReferenceException("Requested service is null");
        }
        public static void RegisterService<T>(Func<T> lazyFunction) where T : class {
            LocatorImpl<T>.Service = new(lazyFunction);
        }
        public static void RegisterService<T>(T value) where T : class {
            LocatorImpl<T>.Service = new(value);
        }
    }
}
