using System;

namespace IntelliVerilog.Core.Expressions {
    public interface IInvertedOutput {
        IoComponent InternalOut { get; }
        Range SelectedRange { get; }
    }
}
