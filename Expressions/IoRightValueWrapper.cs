using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions.Algebra;

namespace IntelliVerilog.Core.Expressions {
    public class IoRightValueWrapper<TData> : RightValue<TData>, IUntypedIoRightValueWrapper where TData : DataType, IDataType<TData> {
        public IUntypedConstructionPort IoComponent { get; }
        public IoComponent UntypedComponent => (IoComponent)IoComponent;
        public IoRightValueWrapper(IUntypedConstructionPort ioPort, IAlg? algebra = null) : base((TData)ioPort.UntypedType, algebra) {
            IoComponent = ioPort;
        }

        public override bool Equals(AbstractValue? other) {
            if(other is IoRightValueWrapper<TData> io) {
                return io.IoComponent == IoComponent;
            }
            return false;
        }
    }
}
