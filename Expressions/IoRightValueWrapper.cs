using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Analysis.TensorLike;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions.Algebra;
using System;
using System.Collections.Immutable;

namespace IntelliVerilog.Core.Expressions {
    public class DummyClockReset : RightValue<Bool> {
        public DummyClockReset() : base(Bool.CreateDefault()) {
        }

        public override Lazy<TensorExpr> TensorExpression => throw new NotImplementedException();

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
        public override Lazy<TensorExpr> TensorExpression { get; }

        public IoRightValueWrapper(IUntypedConstructionPort ioPort, IAlg? algebra = null) : base((TData)ioPort.UntypedType) {
            IoComponent = ioPort;
            TensorExpression = IoComponent.Shape.IsAllDetermined ? MakeTensorExpression() : new Lazy<TensorExpr>(MakeTensorExpression);
        }
        private TensorExpr MakeTensorExpression() {
            return new TensorVarExpr<IUntypedConstructionPort>(IoComponent, IoComponent.Shape.ToImmutableIntShape());
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
