using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Examples.TestSuites {
    public class FiFo:Module<(
        Input<UInt> inValue,
        Output<UInt> output
        )> { 
        public FiFo(uint depth, uint width) {
            ref var io = ref UseDefaultIo(new() { 
                inValue = width.Bits(),
                output = width.Bits()
            });

            Debug.Assert(depth >= 1);

            ref var reg0 = ref Reg.New(width.Bits());
            reg0 = io.inValue.RValue;
            for (var i = 1; i < depth; i++) {
                var stagedValue = reg0;

                reg0 = ref Reg.New(width.Bits())!;
                reg0 = stagedValue;
            }
            io.output = reg0.RValue;
        }
    }
    public class PipelinedMultiplier:Module<(
        Input<UInt> x,
        Input<UInt> y,
        Output<UInt> output
        )> {
        public PipelinedMultiplier(uint width, uint delay) {
            ref var io = ref UseDefaultIo(new() { 
                x = width.Bits(),
                y = width.Bits(),
                output = width.Bits()
            });

            var xFiFo = new FiFo(delay, width);
            xFiFo.IO.inValue = io.x;
            var yFiFo = new FiFo(delay, width);
            yFiFo.IO.inValue = io.x;

            io.output = xFiFo.IO.output * yFiFo.IO.output;
        }
    }
}
