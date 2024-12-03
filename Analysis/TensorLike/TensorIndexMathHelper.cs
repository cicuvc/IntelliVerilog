namespace IntelliVerilog.Core.Analysis.TensorLike {
    public static class TensorIndexMathHelper {
        public static int GreatestCommonDivisor(int a, int b) {
            if(b > a) { var t = a; a = b; b = t; }
            if(b == 0) return a;
            return GreatestCommonDivisor(b, a % b);
        }
    }

}
