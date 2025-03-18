using System;

namespace IntelliVerilog.Core.DataTypes {
    public class EnumEncodeTypeAttribute:Attribute {
        public Type Encoder { get; }
        public EnumEncodeTypeAttribute(Type encoder) {
            Encoder = encoder;
        }
    }
}
