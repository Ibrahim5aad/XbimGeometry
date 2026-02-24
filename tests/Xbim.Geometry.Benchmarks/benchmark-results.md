# Benchmark Results — XbimGeometry Engine Comparison

**Environment:** Windows 11, Intel Core i7-10875H 2.30GHz, 16 logical / 8 physical cores, .NET 8.0.22

| Label | Engine | OCCT |
|-------|--------|------|
| **Old V5** | C++/CLI, v5 API path | 7.8.1 |
| **Old V6** | C++/CLI, v6 factory path | 7.8.1 |
| **New** | P/Invoke, unified | 7.9.3 |

---

## Composite Curves

| Benchmark | Old V5 | Old V6 | New |
|-----------|-------:|-------:|----:|
| CompositeCurve (polyline) | 12.77 μs / 1.55 KB | 13.00 μs / 1.55 KB | **4.62 μs / 6.77 KB** |
| CompositeCurve (mixed arcs+lines) | 61.45 μs / 3.29 KB | 63.00 μs / 3.29 KB | 587 μs / 6.09 KB |

### Diagnostic Breakdown (polyline variant)

| Phase | Time | Allocated |
|-------|-----:|----------:|
| Marshal only | 3.51 μs | 7,016 B |
| Native only | 2.66 μs | 32 B |
| Full end-to-end | 4.41 μs | 6,856 B |

### Diagnostic Breakdown (mixed arcs+lines variant)

| Phase | Time | Allocated |
|-------|-----:|----------:|
| Marshal only | 3.32 μs | 6,464 B |
| Native only | 77.4 μs | 32 B |
| Full end-to-end | 590 μs | 6,169 B |

---

## Tessellation

| Benchmark | Old V5 | Old V6 | New |
|-----------|-------:|-------:|----:|
| TriangulatedFaceSet (basic) | 11.55 ms / 47.63 KB | 11.40 ms / 47.63 KB | **4.09 ms / 24.21 KB** |
| TriangulatedFaceSet (beam) | 239.5 ms / 1,697 KB | 239.6 ms / 1,697 KB | **186.2 ms / 836 KB** |
| PolygonalFaceSet | N/A | N/A | **4.72 ms / 4.77 KB** |

---

## Extruded Solids

| Benchmark | Old V5 | Old V6 | New |
|-----------|-------:|-------:|----:|
| ExtrudedAreaSolid (rectangle) | 111.5 μs / 112 B | 110.1 μs / 112 B | 125.6 μs / 233 B |
| ExtrudedAreaSolid (composite curve) | 3,121 μs / 1,788 B | 3,093 μs / 1,788 B | **722 μs / 10,096 B** |

---

## Boolean Operations

| Benchmark | Old V5 | Old V6 | New |
|-----------|-------:|-------:|----:|
| Boolean simple clip | 15.87 ms / 3.61 KB | 16.24 ms / 3.61 KB | 16.05 ms / 2.52 KB |
| Boolean nested | 29.31 ms / 4.93 KB | 29.15 ms / 4.93 KB | 29.36 ms / 3.43 KB |
| Boolean complex nested | N/A | N/A | **462 ms / 40.06 KB** |

---

## CSG Operations

| Benchmark | Old V5 | Old V6 | New |
|-----------|-------:|-------:|----:|
| CsgPrimitive3D | 48.05 μs / 160 B | 41.03 μs / 112 B | 41.79 μs / 152 B |
| CsgSolid | 12,739 μs / 1,128 B | 12,688 μs / 1,128 B | 14,273 μs / 769 B |

---

## Swept Solids

| Benchmark | Old V5 | Old V6 | New |
|-----------|-------:|-------:|----:|
| SweptDiskSolid | 8,005 μs / 7,058 B | 7,982 μs / 7,058 B | **598 μs / 3,515 B** |
| RevolvedAreaSolid | 11,302 μs / 608 B | 11,340 μs / 608 B | 27,713 μs / 536 B |
| SurfaceCurveSweptAreaSolid | 2,737 μs / 929 B | 2,704 μs / 929 B | 3,075 μs / 4,490 B |

---

## Advanced BRep

| Benchmark | Old V5 | Old V6 | New |
|-----------|-------:|-------:|----:|
| AdvancedBrep (complex) | 76.4 ms / 43.88 KB | 76.2 ms / 43.88 KB | 177.2 ms / 40.52 KB |
| AdvancedBrep (cube) | 6.21 ms / 17.83 KB | N/A | 8.31 ms / 11.13 KB |
| FacetedBrep | 12.12 ms / 108.59 KB | 74.76 ms / 52.94 KB | 81.8 ms / 96.78 KB |

---

## Full Model (Xbim3DModelContext.CreateContext)

| Benchmark | New | Allocated |
|-----------|----:|----------:|
| beam-standard-case | 43.6 ms | 18.27 MB |
| SampleHouse4 | 1,818 ms | 126.68 MB |
