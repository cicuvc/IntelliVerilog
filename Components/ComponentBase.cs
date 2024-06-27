using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace IntelliVerilog.Core.Components {
    public abstract class ComponentBase : IoBundle, ILazyNamedObject, IOverlappedObject {
        public abstract ComponentModel InternalModel { get; }

        public IOverlappedObjectDesc Descriptor { get; set; }

        public abstract bool IsModuleIo(ref byte portReference);
        public abstract bool IsModuleIo(object portReference);

        protected void InitComponentBase() {
            var defaultName = $"M{Utility.GetRandomStringHex(16)}";

            Descriptor = new SubComponentDesc(defaultName) { this };
            Name = () => {
                return $"{Descriptor.InstanceName}_{((SubComponentDesc)Descriptor).IndexOf(this)}";
            };
        }
        public ComponentBase() {
            Descriptor = null!;
            Name = null!;
            InitComponentBase();
        }
    }
}
