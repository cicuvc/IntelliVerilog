using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Examples {
    public class FullAdderIo : IoBundle {
        public Input<Bool> A { get; set; }
        public Input<Bool> B { get; set; }
        public Input<Bool> C { get; set; }
        public Output<Bool> S { get; set; }
        public Output<Bool> CO { get; set; }
    }

    public class FullAdder:Module{
        public FullAdderIo? Io { 
            get; 
            set;
        } 
        public FullAdder() {
            var io = UseIoPorts(Io, new());

            var xorAB = (io.A ^ io.B);

            io.S = xorAB ^ io.C;
            io.CO = (xorAB & io.C) | (io.A & io.B);
        }
    }

    public class Adder : Module<(
            Input<UInt> A,
            Input<UInt> B,
            Output<UInt> S,
            Output<Bool> Cout
            )> {

        public Input<Bool>? Cin { get; set; }
        public Adder(uint width) {
            ref var io = ref UseDefaultIo(new() {
                A = width.Bits(),
                B = width.Bits(),
                S = width.Bits()
            });
            var cin = UseIoPorts(Cin);

            var carryInput = cin.RValue;

            for (var i = 0; i < width; i++) {
                var fullAdder = new FullAdder(){
                    Io = new() {
                        
                        B = io.B[i],
                        C = carryInput,
                        //S = io.S[i]
                    }
                };

                fullAdder.Io.A[0] = io.A[i];
                io.S[i] = fullAdder.Io.S;
                //io.S[i] = fullAdder.Io.S;
                carryInput = fullAdder.Io.CO;
            }
            io.Cout = carryInput;
        }
    }

}
