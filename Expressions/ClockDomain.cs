using IntelliVerilog.Core.Analysis;
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
        protected RightValue<Bool> m_Clock;
        protected RightValue<Bool>? m_Reset;
        protected RightValue<Bool>? m_SyncReset;
        protected RightValue<Bool>? m_ClockEnable;
        internal RightValue<Bool> RawClock => m_Clock;
        internal RightValue<Bool>? RawReset => m_Reset;
        internal RightValue<Bool>? RawSyncReset => m_SyncReset;
        internal RightValue<Bool>? RawClockEnable => m_ClockEnable;
        public RightValue<Bool> Clock { 
            get {
                var model = IntelliVerilogLocator.GetService<AnalysisContext>()!.GetComponentBuildingModel()!;
                if (model == CreationModel) return m_Clock;
                return model.ResolveClockDomainSignal(this, ClockDomainSignal.Clock)!;
            } 
            set {
                var model = IntelliVerilogLocator.GetService<AnalysisContext>()!.GetComponentBuildingModel()!;
                if (model == CreationModel) m_Clock = value;
                throw new UnauthorizedAccessException();
            }
        } 
        public RightValue<Bool>? Reset {
            get {
                var model = IntelliVerilogLocator.GetService<AnalysisContext>()!.GetComponentBuildingModel()!;
                if (model == CreationModel) return m_Reset;
                return model.ResolveClockDomainSignal(this, ClockDomainSignal.Reset)!;
            }
            set {
                var model = IntelliVerilogLocator.GetService<AnalysisContext>()!.GetComponentBuildingModel()!;
                if (model == CreationModel) m_Reset = value;
                else throw new UnauthorizedAccessException();
            }
        }
        public RightValue<Bool>? SyncReset {

            get {
                var model = IntelliVerilogLocator.GetService<AnalysisContext>()!.GetComponentBuildingModel()!;
                if (model == CreationModel) return m_SyncReset;
                return model.ResolveClockDomainSignal(this, ClockDomainSignal.SyncReset);
            }
            set {
                var model = IntelliVerilogLocator.GetService<AnalysisContext>()!.GetComponentBuildingModel()!;
                if (model == CreationModel) m_SyncReset = value;
                else throw new UnauthorizedAccessException();
            }
        }
        public RightValue<Bool>? ClockEnable {
            get {
                var model = IntelliVerilogLocator.GetService<AnalysisContext>()!.GetComponentBuildingModel()!;
                if (model == CreationModel) return m_ClockEnable;
                return model.ResolveClockDomainSignal(this, ClockDomainSignal.ClockEnable);
            }
            set {
                var model = IntelliVerilogLocator.GetService<AnalysisContext>()!.GetComponentBuildingModel()!;
                if (model == CreationModel) m_ClockEnable = value;
                else throw new UnauthorizedAccessException();
            }
        
        }
        public bool ClockRiseEdge { get; set; } = true;
        public bool ResetHighActive { get; set; } = true;
        public bool SyncResetHighActive { get; set; } = true;
        public bool ClockResetHighActive { get; set; } = true;
        public ComponentModel? CreationModel { get; }
        public ClockDomain(string name, RightValue<Bool> clock) {
            CreationModel = IntelliVerilogLocator.GetService<AnalysisContext>()!.GetComponentBuildingModel();
            Name = name;
            m_Clock = clock;
        }
        public bool IsSynchoroizedWith(ClockDomain? other) {
            return other == this;
        }
        public override int GetHashCode() {
            return Name.GetHashCode();
        }
    }
    public static class ClockArea {

        public static IDisposable Begin(ClockDomain domain) {
            return ScopedLocator.RegisterValue<ClockDomain>(domain);
        }
    }
}
