using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnsafeHelpers = System.Runtime.CompilerServices.Unsafe;

namespace IntelliVerilog.Core.Runtime.Core {
    /// <summary>
    /// Utilities for runtime object manipulation, including 
    /// routines for accessing object raw address, pin managed
    /// objects, inspecting object type size&layout and GC pause
    /// 
    /// Use with caution.
    /// </summary>
    public static class ObjectHelpers {
        /// <summary>
        /// Get raw memory address containing the data of 'obj'
        /// </summary>
        /// <param name="obj">The given object reference</param>
        /// <returns>Pointer to the object method table pointer header</returns>
        public unsafe static nint GetObjectAddress(object obj) {
            return UnsafeHelpers.Read<nint>(UnsafeHelpers.AsPointer(ref obj));
        }
        public unsafe static object GetObjectAtAddress(nint address) {
            return UnsafeHelpers.AsRef<object>(&address);
        }
        
    }

}
