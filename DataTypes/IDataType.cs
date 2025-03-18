using IntelliVerilog.Core.DataTypes.Shape;
using System;

namespace IntelliVerilog.Core.DataTypes {
    public interface IDataType<TData> where TData: DataType {
        public static abstract TData CreateDefault();
        public static abstract TData CreateWidth(ReadOnlySpan<ShapeIndexValue> shape);
    }
}
