using System;

namespace IntelliVerilog.Core.Analysis.TensorLike {
    public static class TensorIndexMathHelper {
        public static int GreatestCommonDivisor(int a, int b) {
            if(b > a) { var t = a; a = b; b = t; }
            if(b == 0) return a;
            return GreatestCommonDivisor(b, a % b);
        }
        public static ReadOnlySpan<int> CumulativeProductExclusive(ReadOnlySpan<int> shape) {
            var result = new int[shape.Length];
            var currentValue = 1;
            for(var i=shape.Length - 1; i >= 0; i--) {
                result[i] = currentValue;
                currentValue *= shape[i];
            }
            return result;
        }
        public static ReadOnlySpan<int> CumulativeProductFull(ReadOnlySpan<int> shape) {
            var result = new int[shape.Length + 1];
            var currentValue = result[^1] = 1;
            for(var i = shape.Length - 1; i >= 0; i--) {
                currentValue *= shape[i];
                result[i] = currentValue;
            }
            return result;
        }
        public static ReadOnlySpan<int> CumulativeProductInclusive(ReadOnlySpan<int> shape) {
            var result = new int[shape.Length];
            var currentValue = 1;
            for(var i = shape.Length - 1; i >= 0; i--) {
                currentValue *= shape[i];
                result[i] = currentValue;
            }
            return result;
        }
        public static int Product(ReadOnlySpan<int> shape) {
            var result = 1;
            foreach(var i in shape) result *= i;
            return result;
        }
    }

}
