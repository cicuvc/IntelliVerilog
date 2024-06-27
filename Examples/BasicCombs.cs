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
        public DFF(uint width, ClockDomain? clockDom = null) {
            ref var io = ref UseDefaultIo(new() { 
                inValue = width.Bits(),
                outValue = width.Bits()
            });

            ref var register = ref Reg.New(width.Bits(), clockDom);

            if (io.en.RValue) {
                register = io.inValue.RValue;
            }
            
            io.outValue = register.RValue;
        }
    }
    public class DFFParent : Module<(
        Input<UInt> inValue,
        Input<Bool> en,
        Output<UInt> outValue
        )> { 
        public DFFParent(uint width) {
            ref var io = ref UseDefaultIo(new() {
                inValue = width.Bits(),
            });

            var dff = new DFF(width);
            dff.IO = io;
        }
    }
    public enum TestEnum {
        Hello = 5,
        World = 6
    }
    public class MuxDemo : Module<
        (Input<UInt> a,
        Input<UInt> b,
        Input<UInt> en,
        Output<UInt> output)
        > {
        public MuxDemo(uint bits) {
            ref var io = ref UseDefaultIo(new() {
                a = bits.Bits(),
                b = bits.Bits(),
                en = bits.Bits(),
                output = bits.Bits(),
            });

            var idModule = new IdentiMod[bits];
            for(var i = 0; i < bits; i++) {
                idModule[i] = new IdentiMod(1);
                idModule[i].IO.inValue = io.a[i].Cast<UInt>();
                io.output[i] = idModule[i].IO.outValue[0];
            }

            //if (io.en[0]) {
            //    io.output[0] = io.b[0];
            //}　else {
            //    for (var i = 0; i < bits; i++) {
            //        if (io.en[i]) {
            //            io.output[i] = io.b[i];
            //        }
            //    }
            //}
           
            
        }
    }
}
