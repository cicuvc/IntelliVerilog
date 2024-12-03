using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Examples.TestSuites {
    public class RippleClock :Module{

        public RippleClock() {
            ref var prescaler = ref Reg.New(12u.Bits());

            prescaler = prescaler.RValue + 1;

            var defaultDom = ScopedLocator.GetService<ClockDomain>()!;


            var subDom = new ClockDomain("down", prescaler[0].Cast<Bool>());
            subDom.Reset = defaultDom.Reset;

            var dataReg = new DFF(15, subDom);

            dataReg.IO.en = true.Const();
            dataReg.IO.inValue = 12u.Const();

        }
    }
}
