# Benchmark Results — XbimGeometry Engine Comparison

**Environment:** Windows 11, Intel Core i7-10875H 2.30GHz, 16 logical / 8 physical cores, .NET 8.0.22

| Label | Engine | OCCT |
|-------|--------|------|
| **Old V5** | C++/CLI, v5 API path | 7.8.1 |
| **Old V6** | C++/CLI, v6 factory path | 7.8.1 |
| **New** | P/Invoke, unified | 7.9.3 |

> **Legend:** **bold** = fastest engine for that row

---

## Composite Curves

| Benchmark | Old V5 | Old V6 | New |
|-----------|-------:|-------:|----:|
| CompositeCurve (polyline) | 12.77 μs / 1.55 KB | 13.00 μs / 1.55 KB | **5.06 μs / 6.77 KB** |
| CompositeCurve (mixed arcs+lines) | 61.45 μs / 3.29 KB | 63.00 μs / 3.29 KB | **50.5 μs / 6.11 KB** |

### Diagnostic Breakdown (polyline variant)

| Phase | Time | Allocated |
|-------|-----:|----------:|
| Marshal only | 3.65 μs | 7,016 B |
| Native only | 2.62 μs | 32 B |
| Full end-to-end | 4.75 μs | 6,856 B |

---

## Tessellation

| Benchmark | Old V5 | Old V6 | New |
|-----------|-------:|-------:|----:|
| TriangulatedFaceSet (basic) | 11.55 ms / 47.63 KB | 11.40 ms / 47.63 KB | **3.69 ms / 24.21 KB** |
| TriangulatedFaceSet (beam) | 239.5 ms / 1,697 KB | 239.6 ms / 1,697 KB | **162.7 ms / 861.6 KB** |
| PolygonalFaceSet | N/A | N/A | **4.24 ms / 4.76 KB** |

---

## Extruded Solids

| Benchmark | Old V5 | Old V6 | New |
|-----------|-------:|-------:|----:|
| ExtrudedAreaSolid (rectangle) | 111.5 μs / 112 B | **110.1 μs / 112 B** | 122.1 μs / 233 B |
| ExtrudedAreaSolid (composite curve) | 3,121 μs / 1,788 B | 3,093 μs / 1,788 B | **714.1 μs / 10,096 B** |

### Diagnostic Breakdown (rectangle profile)

| Phase | Time | Allocated |
|-------|-----:|----------:|
| Profile only | 41.80 μs | 80 B |
| Extrude only | 65.70 μs | 32 B |
| Full end-to-end | 118.45 μs | 233 B |

---

## Boolean Operations

| Benchmark | Old V5 | Old V6 | New |
|-----------|-------:|-------:|----:|
| Boolean simple clip | 15.87 ms / 3.61 KB | 16.24 ms / 3.61 KB | **14.76 ms / 2.45 KB** |
| Boolean nested | 29.31 ms / 4.93 KB | 29.15 ms / 4.93 KB | **26.97 ms / 3.42 KB** |
| Boolean complex nested | N/A | N/A | **452.6 ms / 39.73 KB** |

---

## CSG Operations

| Benchmark | Old V5 | Old V6 | New |
|-----------|-------:|-------:|----:|
| CsgPrimitive3D | 48.05 μs / 160 B | 41.03 μs / 112 B | **36.20 μs / 152 B** |
| CsgSolid | 12,739 μs / 1,128 B | **12,688 μs / 1,128 B** | 13,130 μs / 764 B |

---

## Swept Solids

| Benchmark | Old V5 | Old V6 | New |
|-----------|-------:|-------:|----:|
| SweptDiskSolid | 8,005 μs / 7,058 B | 7,982 μs / 7,058 B | **4,523 μs / 3,450 B** |
| RevolvedAreaSolid (tapered) | **11,302 μs / 608 B** | 11,340 μs / 608 B | 24,710 μs / 526 B |
| SurfaceCurveSweptAreaSolid | 2,737 μs / 929 B | 2,704 μs / 929 B | **2,647 μs / 4,488 B** |

### Diagnostic Breakdown (revolved tapered)

| Phase | Time | Allocated |
|-------|-----:|----------:|
| Profiles only (start + end) | 3,036 μs | 251 B |
| Native only (pipe sweep) | 22,455 μs | 54 B |
| Full end-to-end | 25,140 μs | 526 B |

---

## Advanced BRep

| Benchmark | Old V5 | Old V6 | New |
|-----------|-------:|-------:|----:|
| AdvancedBrep (complex) | 76.4 ms / 43.88 KB | **76.2 ms / 43.88 KB** | 185.9 ms / 40.52 KB |
| AdvancedBrep (cube) | **6.21 ms / 17.83 KB** | N/A | 8.23 ms / 11.14 KB |
| FacetedBrep | **12.12 ms / 108.59 KB** | 74.76 ms / 52.94 KB | 69.98 ms / 96.76 KB |

---

## Full Model (Xbim3DModelContext.CreateContext)

### Multi-threaded (default)

| Benchmark | Old V5 | Old V6 | New | Allocated |
|-----------|-------:|-------:|----:|----------:|
| beam-standard-case | 19.94 ms / 18.29 MB | 20.91 ms / 18.28 MB | **19.00 ms / 18.28 MB** | 18.3 MB |
| SampleHouse4 | 1,841 ms / 126.76 MB | 1,719 ms / 126.74 MB | **1,693 ms / 126.72 MB** | 126.7 MB |

### Single-threaded (MaxThreads = 1)

| Benchmark | Old V5 | Old V6 | New | Allocated |
|-----------|-------:|-------:|----:|----------:|
| SampleHouse4 | 3,231 ms / 126.66 MB | **3,116 ms / 126.66 MB** | 3,453 ms / 126.66 MB | 126.7 MB |

---

## Summary

| Category | Winner | New vs Best Old |
|----------|--------|----------------:|
| Composite Curves (polyline) | **New** | ~2.5x faster |
| Composite Curves (mixed) | **New** | ~1.2x faster |
| Tessellation | **New** | ~3x faster |
| Extruded Solids (rect) | Old V6 | ~10% slower |
| Extruded Solids (composite) | **New** | ~4.3x faster |
| Boolean simple clip | ~equal | ~7% faster (within noise) |
| Boolean nested | **New** | ~7% faster |
| CSG Primitive | **New** | ~12% faster |
| CSG Solid | ~equal | ~3% slower (within noise) |
| Swept Solids (mixed) | varies | 2 wins (1.8x, ~2%), 1 loss (2.2x) |
| Advanced BRep (complex) | Old V6 | ~2.4x slower |
| Advanced BRep (cube) | Old V5 | ~1.3x slower |
| FacetedBrep | Old V5 | ~5.8x slower |
| Full Model (MT) | ~equal | ~5% faster (within noise) |
| Full Model (ST) | Old V6 | ~11% slower |
