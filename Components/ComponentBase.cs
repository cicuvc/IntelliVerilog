using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Components {
    public abstract class ComponentBase : IoBundle, ILazyNamedObject {
        public abstract ComponentModel InternalModel { get; }
        public string? CatagoryName { get; set; }
        public Func<string> Name { get; set; } = () => "<unnamed module>";
    }
}
