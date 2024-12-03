using System;

namespace IntelliVerilog.Core.Expressions {
    public interface IInvertedOutput {
        IoComponent InternalOut { get; }
        GenericIndices SelectedRange { get; }
    }
}
