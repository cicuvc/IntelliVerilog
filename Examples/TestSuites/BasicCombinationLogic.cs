using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Examples.TestSuites {
    public class OperatorTest : Module<(
        Input<UInt> x,
        Input<UInt> y,
        Output<UInt> add,
        Output<UInt> sub,
        Output<UInt> mul,
        Output<UInt> div,
        Output<UInt> xor,
        Output<UInt> and,
        Output<UInt> or,
        Output<UInt> not,

        Output<Bool> eq,
        Output<Bool> neq,
        Output<Bool> ge,
        Output<Bool> le,
        Output<Bool> gt,
        Output<Bool> lt
        )> { 
        public OperatorTest(uint width) {
            ref var io = ref UseDefaultIo(new() { 
                x = width.Bits(),
                y = width.Bits()
            });

            io.add = io.x + io.y;
            io.sub = io.x - io.y;
            io.mul = io.x * io.y;
            io.div = io.x / io.y;
            io.xor = io.x ^ io.y;
            io.and = io.x & io.y;
            io.or = io.x | io.y;
            io.not = ~io.x;

            io.eq = io.x == io.y;
            io.neq = io.x != io.y;
            io.le = io.x <= io.y;
            io.lt = io.x < io.y;
            io.ge = io.x >= io.y;
            io.gt = io.x > io.y;
        }
    }
    
}
