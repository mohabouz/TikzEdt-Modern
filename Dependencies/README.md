# Required parser dependencies

This directory contains only dependencies used by the modern editor, not the
original TikzEdt applications or UI projects.

- `TikzParser/`: the token-preserving TikZ parser source and generated ANTLR
  lexer/parser, retained from the original project and built as a .NET 10 WPF
  library. Source copyright/license headers and the original grammar are kept.
- `ParserRuntime/`: the four existing runtime libraries required by that parser:
  ANTLR 2/3, ANTLR 3 Utility, and StringTemplate. These retain their upstream
  copyright and license terms.

The modern app builds these parser sources through a project reference. No
legacy solution, prebuilt TikzParser DLL, or .NET Framework targeting pack is
needed. Original project: https://github.com/mkantem/tikzedt

Native WPF vector preview rendering uses the NuGet dependency
`SharpVectors.Wpf` 1.8.6 (BSD-3-Clause), restored automatically.
Its upstream and third-party notices are in `SharpVectors.LICENSE.txt`, also
included in published application output. Upstream: https://github.com/ElinamLLC/SharpVectors
