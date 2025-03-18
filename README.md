# IntelliVerilog

The IntelliVerilog is an **hardware description language** based on C#.

IntelliVerilog add no new language features to pure C# but provide
expressiveness of a modern prorgamming language to achieve full-
parameterizable circuit generation and produce synthesizable Verilog.

IntelliVerilog is NOT high-level synthesis (HLS) language. IntelliVerilog 
provides a full-level control of timing, piplining, intermediate value evaluation
and register transition, and generates human-readable Verilog/VHDL code.

- Original C# 9.0 syntax and native IntelliCode support from VS
- Compatible with EDA tools. Generates Verilog (probably +VHDL) files
- Flexibility over VHDL, Verilog, and SystemVerilog for expressive power from compilation-time and meta-programming
- Dependency-injection style clock domain management
- One-to-one map from code to RTL
- Allow Object-Oriented Programming and Functional Programming in elaboration and verification
- Torch/numpy-like tensor operations to manage wire bundles
- No scala! I hate everything related to the JVM... emm although Chisel and SpinalHDL are good indeed.

🚧 Development is in progress.