/*
 * xbim_debug_viz.cpp
 *
 * Debug-only interactive 3D shape viewer using OCCT V3d.
 * Compiled only in Debug builds (XBIM_DEBUG_VIZ defined).
 */

#include "xbim_debug_viz.h"

#ifdef XBIM_DEBUG_VIZ
#ifdef _WIN32

// Windows headers must come BEFORE OCCT headers.
// OCCT includes windows.h with WIN32_LEAN_AND_MEAN which strips the windowing API.
#include <windows.h>
#include <windowsx.h>

#include "xbim_shape.h"
#include "xbim_error.h"

#include <AIS_InteractiveContext.hxx>
#include <AIS_Shape.hxx>
#include <Aspect_DisplayConnection.hxx>
#include <BRepBndLib.hxx>
#include <BRepTools.hxx>
#include <Bnd_Box.hxx>
#include <OpenGl_GraphicDriver.hxx>
#include <Quantity_Color.hxx>
#include <Quantity_NameOfColor.hxx>
#include <V3d_View.hxx>
#include <V3d_Viewer.hxx>
#include <WNT_Window.hxx>

#include <cstdio>
#include <string>

// ── Viewer state ────────────────────────────────────────────────────────────

struct DebugViewerState
{
    Handle(V3d_View)               view;
    Handle(AIS_InteractiveContext)  context;
    bool rotating  = false;
    bool panning   = false;
    int  lastX     = 0;
    int  lastY     = 0;
};

// Single global instance — only one debug viewer at a time.
static DebugViewerState g_dbgViewer;

// ── Default color palette for multi-shape viewing ───────────────────────────

static const Quantity_NameOfColor kPalette[] = {
    Quantity_NOC_STEELBLUE1,
    Quantity_NOC_SALMON,
    Quantity_NOC_SEAGREEN3,
    Quantity_NOC_GOLD,
    Quantity_NOC_ORCHID,
    Quantity_NOC_TURQUOISE,
    Quantity_NOC_TOMATO,
    Quantity_NOC_CHARTREUSE2,
};
static const int kPaletteSize = sizeof(kPalette) / sizeof(kPalette[0]);

// ── Win32 window procedure ──────────────────────────────────────────────────

static LRESULT CALLBACK DebugVizWndProc(HWND hwnd, UINT msg,
                                        WPARAM wParam, LPARAM lParam)
{
    auto& v = g_dbgViewer;

    switch (msg)
    {
    case WM_PAINT: {
        PAINTSTRUCT ps;
        BeginPaint(hwnd, &ps);
        if (!v.view.IsNull()) v.view->Redraw();
        EndPaint(hwnd, &ps);
        return 0;
    }

    case WM_SIZE:
        if (!v.view.IsNull()) v.view->MustBeResized();
        return 0;

    // ── Rotation (left button) ──────────────────────────────────────────
    case WM_LBUTTONDOWN:
        v.rotating = true;
        v.lastX = GET_X_LPARAM(lParam);
        v.lastY = GET_Y_LPARAM(lParam);
        v.view->StartRotation(v.lastX, v.lastY);
        SetCapture(hwnd);
        return 0;

    case WM_LBUTTONUP:
        v.rotating = false;
        ReleaseCapture();
        return 0;

    // ── Pan (right button) ──────────────────────────────────────────────
    case WM_RBUTTONDOWN:
        v.panning = true;
        v.lastX = GET_X_LPARAM(lParam);
        v.lastY = GET_Y_LPARAM(lParam);
        SetCapture(hwnd);
        return 0;

    case WM_RBUTTONUP:
        v.panning = false;
        ReleaseCapture();
        return 0;

    // ── Mouse move ──────────────────────────────────────────────────────
    case WM_MOUSEMOVE: {
        int x = GET_X_LPARAM(lParam);
        int y = GET_Y_LPARAM(lParam);
        if (v.rotating && !v.view.IsNull()) {
            v.view->Rotation(x, y);
        }
        if (v.panning && !v.view.IsNull()) {
            v.view->Pan(x - v.lastX, v.lastY - y);
            v.lastX = x;
            v.lastY = y;
        }
        return 0;
    }

    // ── Zoom (mouse wheel) ──────────────────────────────────────────────
    case WM_MOUSEWHEEL: {
        if (v.view.IsNull()) break;
        int delta = GET_WHEEL_DELTA_WPARAM(wParam);
        // Zoom around window center
        RECT rc;
        GetClientRect(hwnd, &rc);
        int cx = (rc.right - rc.left) / 2;
        int cy = (rc.bottom - rc.top) / 2;
        int step = delta > 0 ? 6 : -6;
        v.view->Zoom(cx, cy, cx + step, cy + step);
        return 0;
    }

    // ── Keyboard shortcuts ──────────────────────────────────────────────
    case WM_KEYDOWN:
        if (v.view.IsNull()) break;
        switch (wParam) {
        case 'F':
            v.view->FitAll();
            break;
        case 'W':
            v.context->SetDisplayMode(AIS_WireFrame, Standard_True);
            break;
        case 'S':
            v.context->SetDisplayMode(AIS_Shaded, Standard_True);
            break;
        case 'T':
            v.view->SetProj(V3d_Zpos);
            v.view->FitAll();
            break;
        case 'R':
            v.view->SetProj(V3d_Xneg);
            v.view->FitAll();
            break;
        case 'L':
            v.view->SetProj(V3d_Xpos);
            v.view->FitAll();
            break;
        case VK_ESCAPE:
            DestroyWindow(hwnd);
            break;
        }
        return 0;

    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    }

    return DefWindowProc(hwnd, msg, wParam, lParam);
}

// ── Build window title from shape info ──────────────────────────────────────

static std::string BuildTitle(XbimShapeHandle shape)
{
    if (!shape) return "xbim Debug Viewer";

    const char* typeStr = "Shape";
    switch (shape->shape.ShapeType()) {
    case TopAbs_COMPOUND:  typeStr = "Compound";  break;
    case TopAbs_COMPSOLID: typeStr = "CompSolid"; break;
    case TopAbs_SOLID:     typeStr = "Solid";     break;
    case TopAbs_SHELL:     typeStr = "Shell";     break;
    case TopAbs_FACE:      typeStr = "Face";      break;
    case TopAbs_WIRE:      typeStr = "Wire";      break;
    case TopAbs_EDGE:      typeStr = "Edge";      break;
    case TopAbs_VERTEX:    typeStr = "Vertex";    break;
    default: break;
    }

    Bnd_Box box;
    BRepBndLib::Add(shape->shape, box);
    if (box.IsVoid()) {
        char buf[128];
        snprintf(buf, sizeof(buf), "xbim Debug — %s", typeStr);
        return buf;
    }

    double xmin, ymin, zmin, xmax, ymax, zmax;
    box.Get(xmin, ymin, zmin, xmax, ymax, zmax);
    char buf[256];
    snprintf(buf, sizeof(buf),
             "xbim Debug — %s [%.1f x %.1f x %.1f]",
             typeStr,
             xmax - xmin, ymax - ymin, zmax - zmin);
    return buf;
}

// ── Core viewer logic ───────────────────────────────────────────────────────

static XbimResult RunViewer(
    XbimShapeHandle* shapes, int count,
    const double* r, const double* g, const double* b)
{
    if (!shapes || count <= 0) {
        xbim_set_error("No shapes provided");
        return XBIM_INVALID_ARG;
    }

    for (int i = 0; i < count; ++i) {
        if (!shapes[i]) {
            xbim_set_error("Null shape handle in array");
            return XBIM_INVALID_HANDLE;
        }
    }

    // ── Register window class ───────────────────────────────────────────
    static const char* kClassName = "XbimDebugVizWindow";
    static bool classRegistered = false;

    if (!classRegistered) {
        WNDCLASSEXA wc = {};
        wc.cbSize        = sizeof(wc);
        wc.style         = CS_HREDRAW | CS_VREDRAW | CS_OWNDC;
        wc.lpfnWndProc   = DebugVizWndProc;
        wc.hInstance      = GetModuleHandle(NULL);
        wc.hCursor        = LoadCursor(NULL, IDC_ARROW);
        wc.hbrBackground  = (HBRUSH)(COLOR_WINDOW + 1);
        wc.lpszClassName  = kClassName;
        if (!RegisterClassExA(&wc)) {
            xbim_set_error("Failed to register debug viewer window class");
            return XBIM_ERROR;
        }
        classRegistered = true;
    }

    // ── Create window ───────────────────────────────────────────────────
    std::string title = BuildTitle(shapes[0]);
    if (count > 1) {
        char suffix[32];
        snprintf(suffix, sizeof(suffix), " (+%d shapes)", count - 1);
        title += suffix;
    }

    HWND hwnd = CreateWindowExA(
        0, kClassName, title.c_str(),
        WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT, CW_USEDEFAULT, 1024, 768,
        NULL, NULL, GetModuleHandle(NULL), NULL);

    if (!hwnd) {
        xbim_set_error("Failed to create debug viewer window");
        return XBIM_ERROR;
    }

    // ── Set up OCCT viewer ──────────────────────────────────────────────
    Handle(Aspect_DisplayConnection) displayConn = new Aspect_DisplayConnection();
    Handle(OpenGl_GraphicDriver) driver = new OpenGl_GraphicDriver(displayConn);

    Handle(V3d_Viewer) viewer = new V3d_Viewer(driver);
    viewer->SetDefaultLights();
    viewer->SetLightOn();

    Handle(AIS_InteractiveContext) context = new AIS_InteractiveContext(viewer);

    Handle(WNT_Window) wntWin = new WNT_Window(hwnd);
    Handle(V3d_View) view = viewer->CreateView();
    view->SetWindow(wntWin);

    // Dark gradient background
    view->SetBgGradientColors(
        Quantity_Color(0.23, 0.23, 0.23, Quantity_TOC_RGB),   // top:  #3a3a3a
        Quantity_Color(0.165, 0.165, 0.165, Quantity_TOC_RGB), // bottom: #2a2a2a
        Aspect_GradientFillMethod_Vertical);

    // Coordinate axes triedron in the corner
    view->TriedronDisplay(Aspect_TOTP_LEFT_LOWER, Quantity_NOC_WHITE, 0.12);

    // ── Display shapes ──────────────────────────────────────────────────
    for (int i = 0; i < count; ++i) {
        Handle(AIS_Shape) ais = new AIS_Shape(shapes[i]->shape);

        // Assign color
        Quantity_Color color;
        if (r && g && b) {
            color = Quantity_Color(r[i], g[i], b[i], Quantity_TOC_RGB);
        } else if (count == 1) {
            color = Quantity_Color(0.5, 0.5, 0.5, Quantity_TOC_RGB); // #808080
        } else {
            color = Quantity_Color(kPalette[i % kPaletteSize]);
        }
        ais->SetColor(color);

        // Transparency for overlapping shapes (second shape onward)
        if (count > 1 && i > 0) {
            ais->SetTransparency(0.4);
        }

        context->Display(ais, AIS_Shaded, 0, Standard_False);
    }

    view->FitAll(0.05);
    view->Redraw();

    // Store state for the WndProc
    g_dbgViewer.view    = view;
    g_dbgViewer.context = context;

    // ── Show window and run message loop ────────────────────────────────
    ShowWindow(hwnd, SW_SHOW);
    UpdateWindow(hwnd);

    MSG msg;
    while (GetMessage(&msg, NULL, 0, 0)) {
        TranslateMessage(&msg);
        DispatchMessage(&msg);
    }

    // ── Cleanup ─────────────────────────────────────────────────────────
    g_dbgViewer.view.Nullify();
    g_dbgViewer.context.Nullify();
    g_dbgViewer.rotating = false;
    g_dbgViewer.panning  = false;

    return XBIM_OK;
}

// ── Public API ──────────────────────────────────────────────────────────────

XbimResult XBIM_CALL xbim_debug_view_shape(XbimShapeHandle shape)
{
    return RunViewer(&shape, 1, nullptr, nullptr, nullptr);
}

XbimResult XBIM_CALL xbim_debug_view_shapes(
    XbimShapeHandle* shapes, int count,
    const double* r, const double* g, const double* b)
{
    return RunViewer(shapes, count, r, g, b);
}

XbimResult XBIM_CALL xbim_debug_dump_brep(
    XbimShapeHandle shape, const char* filepath)
{
    if (!shape) {
        xbim_set_error("Null shape handle");
        return XBIM_INVALID_HANDLE;
    }
    if (!filepath || filepath[0] == '\0') {
        xbim_set_error("Empty file path");
        return XBIM_INVALID_ARG;
    }

    if (!BRepTools::Write(shape->shape, filepath)) {
        xbim_set_error("BRepTools::Write failed");
        return XBIM_ERROR;
    }

    return XBIM_OK;
}

#endif /* _WIN32 */

// ── STL dump (cross-platform, no viz libs needed) ───────────────────────────

#include <BRepMesh_IncrementalMesh.hxx>
#include <BRep_Tool.hxx>
#include <Poly_Triangulation.hxx>
#include <TopExp_Explorer.hxx>
#include <TopLoc_Location.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Face.hxx>

#include <cstdint>
#include <fstream>

XbimResult XBIM_CALL xbim_debug_dump_stl(
    XbimShapeHandle shape, const char* filepath, double deflection)
{
    if (!shape) {
        xbim_set_error("Null shape handle");
        return XBIM_INVALID_HANDLE;
    }
    if (!filepath || filepath[0] == '\0') {
        xbim_set_error("Empty file path");
        return XBIM_INVALID_ARG;
    }
    if (deflection <= 0.0) deflection = 0.1;

    // Tessellate
    BRepMesh_IncrementalMesh mesher(shape->shape, deflection);
    if (!mesher.IsDone()) {
        xbim_set_error("BRepMesh_IncrementalMesh failed");
        return XBIM_ERROR;
    }

    // Count triangles
    uint32_t totalTriangles = 0;
    for (TopExp_Explorer ex(shape->shape, TopAbs_FACE); ex.More(); ex.Next()) {
        TopLoc_Location loc;
        auto tri = BRep_Tool::Triangulation(TopoDS::Face(ex.Current()), loc);
        if (!tri.IsNull()) totalTriangles += tri->NbTriangles();
    }

    // Write binary STL
    std::ofstream ofs(filepath, std::ios::binary);
    if (!ofs) {
        xbim_set_error("Cannot open file for writing");
        return XBIM_ERROR;
    }

    // 80-byte header
    char header[80] = {};
    snprintf(header, sizeof(header), "xbim debug viz - %u triangles", totalTriangles);
    ofs.write(header, 80);

    // Triangle count
    ofs.write(reinterpret_cast<const char*>(&totalTriangles), 4);

    // Triangles
    for (TopExp_Explorer ex(shape->shape, TopAbs_FACE); ex.More(); ex.Next()) {
        TopoDS_Face face = TopoDS::Face(ex.Current());
        TopLoc_Location loc;
        auto tri = BRep_Tool::Triangulation(face, loc);
        if (tri.IsNull()) continue;

        gp_Trsf trsf = loc.Transformation();
        bool reversed = (face.Orientation() == TopAbs_REVERSED);

        for (int i = 1; i <= tri->NbTriangles(); ++i) {
            int n1, n2, n3;
            tri->Triangle(i).Get(n1, n2, n3);
            if (reversed) std::swap(n2, n3);

            gp_Pnt p1 = tri->Node(n1).Transformed(trsf);
            gp_Pnt p2 = tri->Node(n2).Transformed(trsf);
            gp_Pnt p3 = tri->Node(n3).Transformed(trsf);

            // Compute face normal
            gp_Vec v1(p1, p2), v2(p1, p3);
            gp_Vec normal = v1.Crossed(v2);
            if (normal.Magnitude() > 1e-10) normal.Normalize();

            // Write: normal (3 floats), 3 vertices (3 floats each), attribute (uint16)
            float data[12] = {
                (float)normal.X(), (float)normal.Y(), (float)normal.Z(),
                (float)p1.X(), (float)p1.Y(), (float)p1.Z(),
                (float)p2.X(), (float)p2.Y(), (float)p2.Z(),
                (float)p3.X(), (float)p3.Y(), (float)p3.Z()
            };
            ofs.write(reinterpret_cast<const char*>(data), 48);
            uint16_t attr = 0;
            ofs.write(reinterpret_cast<const char*>(&attr), 2);
        }
    }

    return XBIM_OK;
}

#endif /* XBIM_DEBUG_VIZ */
