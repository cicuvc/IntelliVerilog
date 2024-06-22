using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Examples {
    public class IdentiMod: Module<(
        Input<UInt> inValue,
        Output<UInt> outValue
        )> {
        public IdentiMod(uint bits) {
            ref var io = ref UseDefaultIo(new() { 
                inValue = bits.Bits(),
                outValue = bits.Bits()
            });

            io.outValue = io.inValue.RValue;
        }
    }
    public class DFF : Module<(
        Input<UInt> inValue,
        Input<Bool> en,
        Output<UInt> outValue
        )> {
        public DFF(uint width, ClockDomain dom2) {
            ref var io = ref UseDefaultIo(new() { 
                inValue = width.Bits(),
                outValue = width.Bits()
            });

            ref var register = ref Reg.New(width.Bits());
            ref var r2 = ref Reg.New(width.Bits(), dom2, true);

            r2 = register.RValue + io.inValue;
            register = io.inValue.RValue;

            io.outValue = r2.RValue;
        }
    }
    public class MuxDemo : Module<
        (Input<UInt> a,
        Input<UInt> b,
        Input<Bool> en,
        Output<UInt> output,
        Output<UInt> o2)
        > {
        public MuxDemo(uint bits) {
            ref var io = ref UseDefaultIo(new() {
                a = bits.Bits(),
                b = bits.Bits(),
                output = bits.Bits(),
                o2 = bits.Bits()
            });

            ref var wireOutput = ref Wire.New(bits.Bits());
            ref var wireData = ref Wire.New(bits.Bits());

            io.o2 = io.a.RValue;
            
            var pmod = new IdentiMod(bits);
            pmod.IO.inValue = wireOutput.RValue;

            wireOutput = io.b.RValue + wireData;
            wireData = io.a.RValue;

            io.output = pmod.IO.outValue;

            if (io.en.RValue & pmod.IO.outValue[0]) {
                wireData = io.b.RValue;
            }
        }
    }
}
