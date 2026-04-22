import { XbimThreeViewer } from "./viewer.js";

const viewers = new Map();

export async function initViewer(canvasId) {
  const canvas = document.getElementById(canvasId);
  if (!canvas) throw new Error(`Canvas element '${canvasId}' not found`);
  const viewer = new XbimThreeViewer(canvas);
  await viewer.init();
  viewers.set(canvasId, viewer);
  return canvasId;
}

export async function loadWexBim(viewerId, streamRef, modelId) {
  const viewer = viewers.get(viewerId);
  if (!viewer) return;
  const buffer = await streamRef.arrayBuffer();
  viewer.loadWexBim(buffer, modelId);
}

export async function loadWexBimFromUrl(viewerId, url, modelId) {
  const viewer = viewers.get(viewerId);
  if (!viewer) return;
  const response = await fetch(url);
  if (!response.ok) throw new Error(`Failed to fetch '${url}': ${response.status}`);
  const buffer = await response.arrayBuffer();
  viewer.loadWexBim(buffer, modelId);
}

export function clearScene(viewerId) {
  viewers.get(viewerId)?.clearScene();
}

export function setBackgroundColor(viewerId, color) {
  viewers.get(viewerId)?.setBackgroundColor(color);
}

export function fitAll(viewerId) {
  viewers.get(viewerId)?.fitAll();
}

export function setProjection(viewerId, orthographic) {
  viewers.get(viewerId)?.setProjection(orthographic);
}

export function getProjection(viewerId) {
  return viewers.get(viewerId)?.getProjection() ?? "perspective";
}

export function setModelVisible(viewerId, modelId, visible) {
  viewers.get(viewerId)?.setModelVisible(modelId, visible);
}

export function removeModel(viewerId, modelId) {
  viewers.get(viewerId)?.removeModel(modelId);
}

// ── Streaming API ──

export function streamBegin(viewerId, modelId) {
  viewers.get(viewerId)?.streamBegin(modelId);
}

export function streamHeader(viewerId, oneMeter, bx, by, bz, sx, sy, sz, productCount) {
  viewers.get(viewerId)?.streamHeader(oneMeter, bx, by, bz, sx, sy, sz, productCount);
}

export function streamStyle(viewerId, id, r, g, b, a) {
  viewers.get(viewerId)?.streamStyle(id, r, g, b, a);
}

export async function streamGeometry(viewerId, id, streamRef) {
  const viewer = viewers.get(viewerId);
  if (!viewer) return;
  const buffer = await streamRef.arrayBuffer();
  viewer.streamGeometry(id, buffer);
}

export function streamInstance(viewerId, productLabel, typeId, geometryId, styleId,
                               transformArray, bx, by, bz, sx, sy, sz) {
  viewers.get(viewerId)?.streamInstance(productLabel, typeId, geometryId, styleId,
    transformArray, bx, by, bz, sx, sy, sz);
}

export function streamComplete(viewerId) {
  viewers.get(viewerId)?.streamComplete();
}

export function dispose(viewerId) {
  const viewer = viewers.get(viewerId);
  if (viewer) {
    viewer.dispose();
    viewers.delete(viewerId);
  }
}
