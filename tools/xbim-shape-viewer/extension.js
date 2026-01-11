const vscode = require('vscode');
const fs = require('fs');
const path = require('path');
const https = require('https');

const DEBUGVIZ = 'Xbim.Geometry.Engine.Interop.Diagnostics.DebugViz'; // only for OCCT viewer (Debug builds)

const THREE_VERSION = '0.128.0';
const THREE_CDN = `https://unpkg.com/three@${THREE_VERSION}`;

let outputChannel;

// ── Three.js local cache ────────────────────────────────────────────────────

function download(url) {
    return new Promise((resolve, reject) => {
        https.get(url, { headers: { 'User-Agent': 'xbim-shape-viewer' } }, res => {
            // Follow redirects
            if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
                return download(res.headers.location).then(resolve, reject);
            }
            if (res.statusCode !== 200) {
                return reject(new Error(`HTTP ${res.statusCode} for ${url}`));
            }
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => resolve(data));
        }).on('error', reject);
    });
}

async function ensureThreeJS(extensionPath) {
    const cacheDir = path.join(extensionPath, '.cache');
    const threePath = path.join(cacheDir, 'three.min.js');
    const controlsPath = path.join(cacheDir, 'OrbitControls.js');

    if (fs.existsSync(threePath) && fs.existsSync(controlsPath)) {
        return { threePath, controlsPath };
    }

    outputChannel.appendLine('[xbim] Downloading Three.js (one-time)...');
    fs.mkdirSync(cacheDir, { recursive: true });

    const threeCode = await download(`${THREE_CDN}/build/three.min.js`);
    fs.writeFileSync(threePath, threeCode);
    outputChannel.appendLine(`[xbim] Cached: ${threePath} (${threeCode.length} bytes)`);

    const controlsCode = await download(`${THREE_CDN}/examples/js/controls/OrbitControls.js`);
    fs.writeFileSync(controlsPath, controlsCode);
    outputChannel.appendLine(`[xbim] Cached: ${controlsPath} (${controlsCode.length} bytes)`);

    return { threePath, controlsPath };
}

// ── Helpers ─────────────────────────────────────────────────────────────────

function getVariableName() {
    const editor = vscode.window.activeTextEditor;
    if (!editor) return null;
    const selection = editor.selection;
    if (!selection.isEmpty) return editor.document.getText(selection);
    const wordRange = editor.document.getWordRangeAtPosition(selection.active);
    return wordRange ? editor.document.getText(wordRange) : null;
}

async function getFrameId(session) {
    if (vscode.debug.activeStackItem && vscode.debug.activeStackItem.frameId != null) {
        return vscode.debug.activeStackItem.frameId;
    }
    const threads = await session.customRequest('threads');
    if (!threads || !threads.threads) return null;
    for (const thread of threads.threads) {
        try {
            const stack = await session.customRequest('stackTrace', {
                threadId: thread.id, startFrame: 0, levels: 1
            });
            if (stack && stack.stackFrames && stack.stackFrames.length > 0) {
                const frame = stack.stackFrames[0];
                if (frame.source && frame.source.path) return frame.id;
            }
        } catch (e) { /* not stopped */ }
    }
    return null;
}

async function debugEval(expression) {
    const session = vscode.debug.activeDebugSession;
    if (!session) {
        vscode.window.showWarningMessage('No active debug session.');
        return null;
    }
    const frameId = await getFrameId(session);
    if (frameId == null) {
        vscode.window.showWarningMessage('No stopped stack frame. Hit a breakpoint first.');
        return null;
    }
    outputChannel.appendLine(`[xbim] Eval: ${expression} (frame ${frameId})`);
    for (const ctx of ['repl', 'watch']) {
        try {
            const response = await session.customRequest('evaluate', {
                expression, frameId, context: ctx
            });
            outputChannel.appendLine(`[xbim] OK (${ctx}): ${JSON.stringify(response)}`);
            return response;
        } catch (err) {
            outputChannel.appendLine(`[xbim] Fail (${ctx}): ${JSON.stringify(err)}`);
        }
    }
    vscode.window.showErrorMessage('Expression evaluation failed. See "xbim Shape Viewer" output.');
    outputChannel.show(true);
    return null;
}

async function evalWriteShape(name, method, filePath) {
    // Try directly first (works when variable is typed as Shape/Solid/etc.)
    await debugEval(`${name}.${method}("${filePath}")`);
    if (fs.existsSync(filePath)) return true;

    // Debugger may see the variable as IXbimGeometryObject — cast to Shape
    outputChannel.appendLine(`[xbim] Direct call failed, casting to XbimShape...`);
    await debugEval(`((Xbim.Geometry.Engine.Interop.Shapes.XbimShape)${name}).${method}("${filePath}")`);
    return fs.existsSync(filePath);
}

// ── Set helpers (XbimSolidSet, XbimGeometryObjectSet) ───────────────────────

async function tryGetSetCount(name) {
    for (const expr of [
        `${name}.Count`,
        `((Xbim.Common.Geometry.IXbimSolidSet)${name}).Count`,
        `((Xbim.Common.Geometry.IXbimGeometryObjectSet)${name}).Count`,
    ]) {
        const result = await debugEval(expr);
        if (result && result.result) {
            const count = parseInt(result.result);
            if (!isNaN(count) && count > 0) return count;
        }
    }
    return null;
}

async function evalWriteSetStl(name, basePath, count) {
    const paths = [];
    for (let i = 0; i < count; i++) {
        const stlPath = basePath.replace('.stl', `_${i}.stl`);
        for (const expr of [
            `((Xbim.Geometry.Engine.Interop.Shapes.XbimShape)System.Linq.Enumerable.ElementAt(${name}, ${i})).WriteStl("${stlPath}")`,
            `((Xbim.Geometry.Engine.Interop.Shapes.XbimShape)${name}.ElementAt(${i})).WriteStl("${stlPath}")`,
            `((Xbim.Geometry.Engine.Interop.Shapes.XbimShape)System.Linq.Enumerable.ElementAt((System.Collections.Generic.IEnumerable<Xbim.Common.Geometry.IXbimGeometryObject>)${name}, ${i})).WriteStl("${stlPath}")`,
        ]) {
            await debugEval(expr);
            if (fs.existsSync(stlPath)) break;
        }
        if (fs.existsSync(stlPath)) {
            paths.push(stlPath);
        } else {
            outputChannel.appendLine(`[xbim] Failed to write STL for element ${i}`);
        }
    }
    return paths;
}

// ── WebView Panel ───────────────────────────────────────────────────────────

async function createViewerPanel(context, variableName, stlBase64) {
    let libs;
    try {
        libs = await ensureThreeJS(context.extensionPath);
    } catch (err) {
        vscode.window.showErrorMessage(`Failed to download Three.js: ${err.message}`);
        return;
    }

    const panel = vscode.window.createWebviewPanel(
        'xbimShapeViewer',
        `${variableName}`,
        vscode.ViewColumn.Beside,
        {
            enableScripts: true,
            retainContextWhenHidden: true,
            localResourceRoots: [
                vscode.Uri.file(path.join(context.extensionPath, '.cache'))
            ]
        }
    );

    const threeUri = panel.webview.asWebviewUri(vscode.Uri.file(libs.threePath));
    const controlsUri = panel.webview.asWebviewUri(vscode.Uri.file(libs.controlsPath));
    const cspSource = panel.webview.cspSource;

    const htmlPath = path.join(context.extensionPath, 'viewer.html');
    let html = fs.readFileSync(htmlPath, 'utf8');
    html = html.replace('{{threeUri}}', threeUri);
    html = html.replace('{{controlsUri}}', controlsUri);
    html = html.replace('{{cspSource}}', cspSource);

    panel.webview.html = html;

    panel.webview.onDidReceiveMessage(
        message => {
            if (message.type === 'ready') {
                if (Array.isArray(stlBase64)) {
                    panel.webview.postMessage({ type: 'loadMultiSTL', data: stlBase64 });
                } else {
                    panel.webview.postMessage({ type: 'loadSTL', data: stlBase64 });
                }
            }
        },
        undefined,
        context.subscriptions
    );

    return panel;
}

// ── Commands ────────────────────────────────────────────────────────────────

function activate(context) {
    outputChannel = vscode.window.createOutputChannel('xbim Shape Viewer');

    // Pre-download Three.js in background
    ensureThreeJS(context.extensionPath).catch(() => {});

    // ── Inline 3D panel (Ctrl+Alt+V) ────────────────────────────────────
    context.subscriptions.push(
        vscode.commands.registerCommand('xbim.viewShapeInline', async () => {
            const name = getVariableName();
            if (!name) {
                vscode.window.showWarningMessage('Place cursor on or select a shape variable name.');
                return;
            }

            await vscode.window.withProgress(
                { location: vscode.ProgressLocation.Notification, title: `Rendering "${name}"...` },
                async () => {
                    const stlPath = path.join(require('os').tmpdir(), `xbim_${name}_${Date.now()}.stl`).replace(/\\/g, '/');

                    // Try single shape first
                    const ok = await evalWriteShape(name, 'WriteStl', stlPath);
                    outputChannel.appendLine(`[xbim] STL: ${stlPath}`);

                    if (ok) {
                        const stlBase64 = fs.readFileSync(stlPath).toString('base64');
                        outputChannel.appendLine(`[xbim] STL size: ${fs.statSync(stlPath).size} bytes`);
                        await createViewerPanel(context, name, stlBase64);
                        return;
                    }

                    // Try as geometry set (XbimSolidSet / XbimGeometryObjectSet)
                    outputChannel.appendLine(`[xbim] Single shape failed, trying as geometry set...`);
                    const count = await tryGetSetCount(name);

                    if (count != null) {
                        outputChannel.appendLine(`[xbim] Geometry set detected: ${count} elements`);
                        const stlPaths = await evalWriteSetStl(name, stlPath, count);

                        if (stlPaths.length > 0) {
                            const stlDataArray = stlPaths.map(p => {
                                outputChannel.appendLine(`[xbim] STL: ${p} (${fs.statSync(p).size} bytes)`);
                                return fs.readFileSync(p).toString('base64');
                            });
                            await createViewerPanel(context, name, stlDataArray);
                            return;
                        }
                    }

                    vscode.window.showErrorMessage(`STL file not created. Check "xbim Shape Viewer" output.`);
                    outputChannel.show(true);
                }
            );
        })
    );

    // ── OCCT native window (Ctrl+Alt+Shift+V) ──────────────────────────
    context.subscriptions.push(
        vscode.commands.registerCommand('xbim.viewShape', async () => {
            const name = getVariableName();
            if (!name) {
                vscode.window.showWarningMessage('Place cursor on or select a shape variable name.');
                return;
            }
            vscode.window.withProgress(
                { location: vscode.ProgressLocation.Notification, title: `Opening OCCT viewer for "${name}"...` },
                async () => {
                    const result = await debugEval(`${DEBUGVIZ}.Show(${name})`);
                    if (result) return;

                    outputChannel.appendLine(`[xbim] Direct Show failed, casting to XbimShape...`);
                    const castResult = await debugEval(`${DEBUGVIZ}.Show((Xbim.Geometry.Engine.Interop.Shapes.XbimShape)${name})`);
                    if (castResult) return;

                    // Try as geometry set (XbimSolidSet / XbimGeometryObjectSet)
                    outputChannel.appendLine(`[xbim] Cast failed, trying as geometry set...`);
                    const count = await tryGetSetCount(name);
                    if (count != null) {
                        outputChannel.appendLine(`[xbim] Geometry set: showing ${count} elements`);
                        for (let i = 0; i < count; i++) {
                            for (const expr of [
                                `${DEBUGVIZ}.Show((Xbim.Geometry.Engine.Interop.Shapes.XbimShape)System.Linq.Enumerable.ElementAt(${name}, ${i}))`,
                                `${DEBUGVIZ}.Show((Xbim.Geometry.Engine.Interop.Shapes.XbimShape)System.Linq.Enumerable.ElementAt((System.Collections.Generic.IEnumerable<Xbim.Common.Geometry.IXbimGeometryObject>)${name}, ${i}))`,
                            ]) {
                                const r = await debugEval(expr);
                                if (r) break;
                            }
                        }
                    }
                }
            );
        })
    );

    // ── Dump BREP ───────────────────────────────────────────────────────
    context.subscriptions.push(
        vscode.commands.registerCommand('xbim.dumpBrep', async () => {
            const name = getVariableName();
            if (!name) {
                vscode.window.showWarningMessage('Place cursor on or select a shape variable name.');
                return;
            }

            const uri = await vscode.window.showSaveDialog({
                defaultUri: vscode.Uri.file(path.join(require('os').homedir(), `${name}.brep`)),
                filters: { 'BREP files': ['brep'], 'All files': ['*'] },
                title: 'Save BREP file'
            });
            if (!uri) return; // cancelled

            const savePath = uri.fsPath.replace(/\\/g, '/');
            const ok = await evalWriteShape(name, 'WriteBrep', savePath);

            if (ok) {
                const action = await vscode.window.showInformationMessage(
                    `BREP saved to: ${uri.fsPath}`, 'Open Folder');
                if (action === 'Open Folder') {
                    vscode.commands.executeCommand('revealFileInOS',
                        vscode.Uri.file(path.dirname(uri.fsPath)));
                }
                return;
            }

            // Try as geometry set — save each element as name_0.brep, name_1.brep, etc.
            outputChannel.appendLine(`[xbim] Single shape BREP failed, trying as geometry set...`);
            const count = await tryGetSetCount(name);
            if (count != null) {
                outputChannel.appendLine(`[xbim] Geometry set: writing ${count} BREP files`);
                const dir = path.dirname(savePath);
                const base = path.basename(savePath, '.brep');
                let written = 0;
                for (let i = 0; i < count; i++) {
                    const solidPath = path.join(dir, `${base}_${i}.brep`).replace(/\\/g, '/');
                    for (const expr of [
                        `((Xbim.Geometry.Engine.Interop.Shapes.XbimShape)System.Linq.Enumerable.ElementAt(${name}, ${i})).WriteBrep("${solidPath}")`,
                        `((Xbim.Geometry.Engine.Interop.Shapes.XbimShape)${name}.ElementAt(${i})).WriteBrep("${solidPath}")`,
                        `((Xbim.Geometry.Engine.Interop.Shapes.XbimShape)System.Linq.Enumerable.ElementAt((System.Collections.Generic.IEnumerable<Xbim.Common.Geometry.IXbimGeometryObject>)${name}, ${i})).WriteBrep("${solidPath}")`,
                    ]) {
                        await debugEval(expr);
                        if (fs.existsSync(solidPath)) { written++; break; }
                    }
                }
                if (written > 0) {
                    const action = await vscode.window.showInformationMessage(
                        `${written} BREP files saved to: ${path.dirname(uri.fsPath)}`, 'Open Folder');
                    if (action === 'Open Folder') {
                        vscode.commands.executeCommand('revealFileInOS',
                            vscode.Uri.file(path.dirname(uri.fsPath)));
                    }
                    return;
                }
            }

            vscode.window.showErrorMessage(`BREP file was not created. Check "xbim Shape Viewer" output for details.`);
            outputChannel.show(true);
        })
    );
}

function deactivate() {
    if (outputChannel) outputChannel.dispose();
}

module.exports = { activate, deactivate };
