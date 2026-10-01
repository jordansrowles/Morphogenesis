# Rowles.StrictMaths

`Rowles.StrictMaths` is a small low-level library for reproducibility-sensitive elementary functions. It targets `net10.0`, is marked AOT compatible, has no runtime package dependency and does not call native maths libraries.

## Algorithms and provenance

The exponential and logarithm approximations, coefficients and argument reductions follow FDLIBM 5.3 `e_exp.c` and `e_log.c`. Sine and cosine use the FDLIBM 5.3 `e_rem_pio2.c`, `k_sin.c` and `k_cos.c` methods and coefficients. Their range reduction stores 1400-bit integer expansions of `2/pi` and `pi/2`, covering every finite binary64 input. Square root uses an integer square-root construction with exact remainder-based rounding.

FDLIBM 5.3 was developed at Sun Microsystems. Its source grants permission to use, copy, modify and distribute the software provided that its notice is retained; see [NOTICE](NOTICE) and the [FDLIBM 5.3 source archive](https://www.netlib.org/fdlibm/).

## Result contract

All functions operate on IEEE-754 binary64 values and use fixed constants and explicit reduction. `Exp`, `Log`, `Sin` and `Cos` follow the FDLIBM error target of less than one ULP. `Sqrt` rounds correctly to nearest, ties to even. Signed zero is retained where the function defines it; NaN inputs are returned unchanged where possible; domain-invalid inputs return NaN; and infinities follow the corresponding IEEE elementary-function rules.

The same committed exact-bit vectors and separate offline high-precision reference vectors are run on Linux and Windows. Ordinary double arithmetic is used between function calls; there is no replacement floating-point type or approximate SIMD path.

## Public API

The small public surface is `StrictMath.Exp`, `StrictMath.Log`, `StrictMath.Sqrt`, `StrictMath.Sin`, `StrictMath.Cos` and `StrictMath.SinCos`.
