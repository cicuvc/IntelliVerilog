using IntelliVerilog.Core.DataTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Expressions {
    public class ClockDomain {
        public string Name { get; }
        public ulong ReferenceFrequency { get; }
        public RightValue<Bool> Clock { get; set; } 
        public RightValue<Bool>? Reset { get; set; }
        public RightValue<Bool>? SyncReset { get; set; }
        public RightValue<Bool>? ClockEnable { get; set; }
        public bool ClockRiseEdge { get; set; } = true;
        public bool ResetHighActive { get; set; } = true;
        public bool SyncResetHighActive { get; set; } = true;
        public bool ClockResetHighActive { get; set; } = true;
        public ClockDomain(string name, RightValue<Bool> clock) {
            Name = name;
            Clock = clock;
        }
        public bool IsSynchoroizedWith(ClockDomain? other) {
            return other == this;
        }
    }
    public static class ClockArea {

        public static IDisposable Begin(ClockDomain domain) {
            return ScopedLocator.RegisterValue<ClockDomain>(domain);
        }
    }
}
