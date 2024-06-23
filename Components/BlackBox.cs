using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Components {
    [ModuleCompilerIgnore]
    public class BlackBox<TIoPorts> : Module<TIoPorts>, ITupledModule where TIoPorts:struct, ITuple {
        protected void DefineOutputClockDomain() {

        }
        protected void DefineCombinationIo() {

        }
    }
}
