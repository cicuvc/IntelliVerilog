using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.DataTypes.Shape;
using IntelliVerilog.Core.Expressions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Examples.TestSuites {
    public class TestMem<T>:Module<(
        Input<T> inVal,
        Output<T> outVal
        )> where T:DataType, IDataType<T> {
        public TestMem(T dataType) {
            ref var io = ref UseDefaultIo(new() { 
                inVal = dataType
            });

            ref var register = ref Reg.New<T>(dataType);

            register = io.inVal.RValue;
            io.outVal = register.RValue;
        }
    }
    public static class TestBFExt {
        public static ref RightValue<UInt> NextAddress(this IRightValueConvertible<TestBF> thisObject) {
            var model = IntelliVerilogLocator.GetService<AnalysisContext>()!.GetComponentBuildingModel();
            var heapPtr = model.RegisterHeapPointer(thisObject.RValue.Cast<UInt>()[0..6]);

            return ref heapPtr.AsRef<RightValue<UInt>>();
        }
        public static ref RightValue<UInt> N2Address(this IRightValueConvertible<TestBF> thisObject) {
            var model = IntelliVerilogLocator.GetService<AnalysisContext>()!.GetComponentBuildingModel();
            var heapPtr = model.RegisterHeapPointer(thisObject.RValue.Cast<UInt>()[6..12]);

            return ref heapPtr.AsRef<RightValue<UInt>>();
        }
    }

    public class TestBF : UInt,IDataType<TestBF> {
        public static TestBF DefaultType { get; } = new([12]);
        public TestBF(ReadOnlySpan<ShapeIndexValue> shape) : base(shape) {
        }
        static TestBF IDataType<TestBF>.CreateDefault() => DefaultType;

        static TestBF IDataType<TestBF>.CreateWidth(ReadOnlySpan<ShapeIndexValue> shape)
            => DefaultType;
    }
    public class TestOut : Module<(
        Input<UInt> inPort,
        Output<TestBF> outPort
        )> {
        public TestOut() {
            ref var io = ref UseDefaultIo(new() {
                inPort = 5u.Bits()
            });
            io.outPort.NextAddress() = 3u.Const();
            io.outPort.N2Address() = 6u.Const();
        }
    }
    public class Test:Module<(
        Input<TestBF> inPort,
        Output<UInt> outPort
        )> {
        public Test() {
            ref var io = ref UseDefaultIo(new() {
                outPort = 6u.Bits()
            });
            io.outPort = io.inPort.NextAddress();
        }
    }
    public class Test2 : Module<(
        Input<TestBF> inPort,
        Output<UInt> outPort
        )> {
        public Test2() {
            ref var io = ref UseDefaultIo(new() {
                outPort = 6u.Bits()
            });
            var m2 = new Test();
            m2.IO.inPort = io.inPort;
            io.outPort = m2.IO.outPort;
        }
    }
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
                reg0 = ref Reg.Next(width.Bits(), reg0.RValue)!;
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
            yFiFo.IO.inValue = io.y;

            io.output = xFiFo.IO.output * yFiFo.IO.output;
        }
    }
}
