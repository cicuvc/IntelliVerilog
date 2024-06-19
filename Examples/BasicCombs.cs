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

            io.o2 = io.a.RValue;
            io.output = wireOutput.RValue;

            wireOutput = io.b.RValue;

            if (io.en.RValue) {
                wireOutput[1] = io.a.RValue[1];
            }
        }
    }
}
