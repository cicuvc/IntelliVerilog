using IntelliVerilog.Core.DataTypes.Shape;
using IntelliVerilog.Core.Expressions;
using IntelliVerilog.Core.Expressions.Algebra;
using System;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.DataTypes {

    public interface IAutoWidthAdjustType {
        AbstractValue CreateExpansion(AbstractValue value, Size newWidth);
        AbstractValue CreateShrink(AbstractValue value, Size newWidth);
    }
    
    public interface IAutoWidthAdjustType<TData>: IAutoWidthAdjustType where TData:DataType<TData>, IDataType<TData> {
        RightValue<TData> CreateExpansion(RightValue<TData> value, Size newWidth);
        RightValue<TData> CreateShrink(RightValue<TData> value, Size newWidth);
    }
    
    public abstract class DataType :IEquatable<DataType> {
        public Size Shape { get; }
        public abstract Size VecShape { get; }
        public abstract Size BitWidth { get; }
        public bool IsShapeComplete => Shape.IsAllDetermined;
        
        public DataType(ReadOnlySpan<ShapeIndexValue> shape) {
            Shape = new(shape);
        }
        public abstract IAlg DefaultAlgebra { get; }
        public abstract DataType? CreateDefaultType();
        public abstract DataType CreateShapedType(ReadOnlySpan<ShapeIndexValue> shape);
        public bool Equals(DataType? other) {
            if(other?.GetType() != GetType()) return false;
            return Shape.Equals(other.Shape);
        }
    }
    public abstract class DataType<TData> : DataType where TData : DataType<TData>, IDataType<TData> {
        protected DataType(ReadOnlySpan<ShapeIndexValue> shape) : base(shape) {
        }
        public override DataType? CreateDefaultType() => TData.CreateDefault();
        public override DataType CreateShapedType(ReadOnlySpan<ShapeIndexValue> shape) => TData.CreateWidth(shape);
    }
}
