using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Components {
    public abstract class ComponentBase : IoBundle {
        public abstract ComponentModel InternalModel { get; }
        public string? CatagoryName { get; set; } 
    }
}
