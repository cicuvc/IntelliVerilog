using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions.Algebra;
using System;

namespace IntelliVerilog.Core.Expressions {
    public class DummyClockReset : RightValue<Bool> {
        public DummyClockReset() : base(Bool.CreateDefault()) {
        }

        public override void EnumerateSubNodes(Action<AbstractValue> callback) {
            throw new NotImplementedException();
        }

        public override bool Equals(AbstractValue? other) {
            throw new NotImplementedException();
        }
    }
    public class IoRightValueWrapper<TData> : RightValue<TData>, IUntypedIoRightValueWrapper where TData : DataType, IDataType<TData> {
        public IUntypedConstructionPort IoComponent { get; }
        public IoComponent UntypedComponent => (IoComponent)IoComponent;
        public override DataType Type { 
            get => base.Type;
            set {
                base.Type = value;
                //IoComponent.ty
            }
        }
        public IoRightValueWrapper(IUntypedConstructionPort ioPort, IAlg? algebra = null) : base((TData)ioPort.UntypedType, algebra) {
            IoComponent = ioPort;
        }

        public override bool Equals(AbstractValue? other) {
            if(other is IoRightValueWrapper<TData> io) {
                return io.IoComponent == IoComponent;
            }
            return false;
        }

        public override void EnumerateSubNodes(Action<AbstractValue> callback) { }
    }
}
