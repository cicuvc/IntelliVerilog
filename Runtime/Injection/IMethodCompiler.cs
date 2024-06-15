using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Runtime.Injection {
    public unsafe interface IMethodCompiler {
        bool Match(MethodBase method, IntPtr methodInfo, out object? context);
        int CompileMethod(object? context, MethodBase method, IntPtr pthis, IntPtr corinfo, JitCompileInfo* methodInfo, uint flags, IntPtr* native, uint* size);
    }
}
