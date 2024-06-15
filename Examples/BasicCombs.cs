using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Examples {
    public class MuxDemo : Module<
        (Input<UInt> a,
        Input<UInt> b,
        Input<Bool> en,
        Output<UInt> output)
        > {
        public MuxDemo(uint bits) {
            ref var io = ref UseDefaultIo(new() {
                a = bits.Bits(),
                b = bits.Bits(),
                output = bits.Bits()
            });

            if (io.en.RValue) {
                io.output = io.a.RValue;
            } else {
                io.output = io.b.RValue;
            }
        }
    }
}
