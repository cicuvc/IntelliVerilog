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
    public class Identical : Module<(
        Input<UInt> way1In,
        Input<UInt> way2In,
        Output<UInt> way1,
        Output<UInt> way2)> { 
        public Identical(uint width) {
            ref var io = ref UseDefaultIo(new() {
                way1In = width.Bits(),
                way2In = width.Bits(),
                way1 = width.Bits(),
                way2 = width.Bits()
            });

            var splitter = new Splitter(width);
            splitter.IO.twoWay[0] = io.way1In[GenericIndex.None];
            splitter.IO.twoWay[1] = io.way2In[GenericIndex.None];

            io.way1 = splitter.IO.way1;
            io.way2 = splitter.IO.way2;
        }
    }
    public class Splitter:Module<(
        Input<UInt> twoWay,
        Output<UInt> way1,
        Output<UInt> way2)> {
        public Splitter(uint width) {
            ref var io = ref UseDefaultIo(new() { 
                twoWay = width.Bits([2]),
                way1 = width.Bits(),
                way2 = width.Bits()
            });

            io.way1 = io.twoWay[0];
            io.way2 = io.twoWay[1];
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

            ref var register = ref Reg.New(width.Bits(), clockDom: clockDom);

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
                io.output[i] = idModule[i].IO.outValue[0].Cast<UInt>();
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
