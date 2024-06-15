using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Runtime.Services {
    public interface IRuntimeDebugInfoService {
        nint FindGlobalFunctionEntry(string name);
        nint FindGlobalVariableAddress(string name);
        IRuntimeTypeInfoService? FindType(string name);
    }
    public interface IRuntimeTypeInfoService {
        bool GetFieldOffset(string name, out uint offset);
        bool GetVirtualMethodOffset(string name, out uint offset);
    }
}
