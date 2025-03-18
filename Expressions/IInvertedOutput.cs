using System;
using System.Collections.Immutable;

namespace IntelliVerilog.Core.Expressions {
    public interface IInvertedOutput {
        IoComponent InternalOut { get; }
        ImmutableArray<GenericIndex> SelectedRange { get; }
    }
}
