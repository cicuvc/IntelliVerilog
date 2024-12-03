using IntelliVerilog.Core.DataTypes;
using System;

namespace IntelliVerilog.Core.Expressions {
    public class ExpressedOutput<TData> : 
        TypeSpecifiedOutput<TData>, IExpressionAssignedIoType where TData : DataType, IDataType<TData> {
        public RightValue<TData> Expression { get; init; }
        public AbstractValue UntypedExpression => Expression;
        public override AbstractValue UntypedRValue => UntypedExpression;
        public override GeneralizedPortFlags Flags => GeneralizedPortFlags.SingleComponent | GeneralizedPortFlags.WidthSpecified;

        public override RightValue<TData> this[params GenericIndex[] range] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public ExpressedOutput(RightValue<TData> expression) : base(expression.TypedType, expression.Shape) {
            Expression = expression;
        }
    }
}
