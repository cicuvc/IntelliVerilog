using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Runtime.Services {
    public interface IDemangleSerivce {
        string GetDemangledName(string signature);

    }
}
