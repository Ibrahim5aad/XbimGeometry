import * as THREE from "three/webgpu";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";
import { WexBIMLoader } from "./wexBimLoader.js";
import { StreamingSceneBuilder } from "./streamingSceneBuilder.js";

export class XbimThreeViewer {
  #renderer;
  #scene;
  #camera;
  #controls;
  #canvas;
  #resizeObserver;
  #streamBuilder = null;
  #initialFitDone = false;
  #models = new Map(); // modelId -> THREE.Group

  constructor(canvas) {
    this.#canvas = canvas;
  }

  async init() {
    this.#renderer = new THREE.WebGPURenderer({ canvas: this.#canvas, antialias: true });
    this.#renderer.setPixelRatio(window.devicePixelRatio);
    this.#renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.#renderer.toneMappingExposure = 1.2;

    await this.#renderer.init();

    this.#scene = new THREE.Scene();
    this.#scene.background = new THREE.Color(0x0a0a1a);

    this.#camera = new THREE.PerspectiveCamera(50, 1, 0.1, 100000);

    this.#controls = new OrbitControls(this.#camera, this.#canvas);
    this.#controls.enableDamping = true;
    this.#controls.dampingFactor = 0.1;

    this.#scene.add(new THREE.AmbientLight(0xffffff, 0.5));
    const dir1 = new THREE.DirectionalLight(0xffffff, 0.9);
    dir1.position.set(1, 2, 1.5).normalize();
    this.#scene.add(dir1);
    const dir2 = new THREE.DirectionalLight(0xffffff, 0.3);
    dir2.position.set(-1, -0.5, -1).normalize();
    this.#scene.add(dir2);

    this.#resizeObserver = new ResizeObserver(() => this.resize());
    this.#resizeObserver.observe(this.#canvas.parentElement);

    this.resize();
    this.#renderer.setAnimationLoop(() => this.#animate());
  }

  clearScene() {
    for (const group of this.#models.values()) {
      this.#disposeGroup(group);
      this.#scene.remove(group);
    }
    this.#models.clear();
    this.#removeGrid();
    if (this.#streamBuilder) {
      this.#streamBuilder.dispose();
      this.#streamBuilder = null;
    }
  }

  resize() {
    if (!this.#canvas || !this.#renderer) return;
    const parent = this.#canvas.parentElement;
    const w = parent.clientWidth;
    const h = parent.clientHeight;
    if (w === 0 || h === 0) return;
    this.#renderer.setSize(w, h, false);
    this.#canvas.style.width = w + "px";
    this.#canvas.style.height = h + "px";
    this.#camera.aspect = w / h;
    this.#camera.updateProjectionMatrix();
  }

  fitToBox(box) {
    if (box.isEmpty()) return;
    const center = box.getCenter(new THREE.Vector3());
    const size = box.getSize(new THREE.Vector3());
    const maxDim = Math.max(size.x, size.y, size.z);
    const dist = maxDim * 1.5;

    this.#camera.position.set(center.x + dist * 0.7, center.y + dist * 0.5, center.z + dist * 0.7);
    this.#camera.lookAt(center);
    this.#camera.near = Math.max(0.01, maxDim * 0.001);
    this.#camera.far = maxDim * 100;
    this.#camera.updateProjectionMatrix();

    this.#controls.target.copy(center);
    this.#controls.minDistance = maxDim * 0.05;
    this.#controls.maxDistance = maxDim * 10;
    this.#controls.update();
  }

  addGrid(box) {
    if (box.isEmpty()) return;
    const size = box.getSize(new THREE.Vector3());
    const center = box.getCenter(new THREE.Vector3());
    const gridSize = Math.max(size.x, size.z) * 2;
    const grid = new THREE.GridHelper(gridSize, 40, 0x1a2744, 0x111b2e);
    grid.name = "ground-grid";
    grid.position.set(center.x, box.min.y, center.z);
    this.#scene.add(grid);
  }

  loadWexBim(arrayBuffer, modelId) {
    const loader = new WexBIMLoader();
    const group = loader.parse(arrayBuffer);
    group.userData.modelId = modelId;
    this.#models.set(modelId, group);
    this.#scene.add(group);
    this.#refitScene();
  }

  setModelVisible(modelId, visible) {
    const group = this.#models.get(modelId);
    if (group) group.visible = visible;
  }

  removeModel(modelId) {
    const group = this.#models.get(modelId);
    if (!group) return;
    this.#disposeGroup(group);
    this.#scene.remove(group);
    this.#models.delete(modelId);
    if (this.#models.size > 0) {
      this.#refitScene();
    } else {
      this.#removeGrid();
    }
  }

  // ── Streaming API ──

  streamBegin(modelId) {
    this.#streamBuilder = new StreamingSceneBuilder(this.#scene, modelId);
    this.#initialFitDone = false;
  }

  streamHeader(oneMeter, bx, by, bz, sx, sy, sz, productCount) {
    this.#streamBuilder?.setHeader(oneMeter, bx, by, bz, sx, sy, sz, productCount);
  }

  streamStyle(id, r, g, b, a) {
    this.#streamBuilder?.addStyle(id, r, g, b, a);
  }

  streamGeometry(id, buffer) {
    if (!this.#streamBuilder) return;
    this.#streamBuilder.addGeometry(id, buffer);
  }

  streamInstance(productLabel, typeId, geometryId, styleId, transformArray,
                 bx, by, bz, sx, sy, sz) {
    this.#streamBuilder?.addInstance(productLabel, typeId, geometryId, styleId,
      transformArray, bx, by, bz, sx, sy, sz);

    if (!this.#initialFitDone && this.#streamBuilder && !this.#streamBuilder.bounds.isEmpty()) {
      const box = this.#streamBuilder.bounds;
      const center = box.getCenter(new THREE.Vector3());
      const size = box.getSize(new THREE.Vector3());
      const maxDim = Math.max(size.x, size.y, size.z);
      const dist = maxDim * 5;

      this.#camera.position.set(center.x + dist * 0.7, center.y + dist * 0.5, center.z + dist * 0.7);
      this.#camera.lookAt(center);
      this.#camera.near = Math.max(0.01, maxDim * 0.001);
      this.#camera.far = maxDim * 500;
      this.#camera.updateProjectionMatrix();
      this.#controls.target.copy(center);
      this.#controls.update();

      this.#initialFitDone = true;
    }
  }

  streamComplete() {
    if (!this.#streamBuilder) return;
    this.#streamBuilder.complete();
    const group = this.#streamBuilder.group;
    const modelId = this.#streamBuilder.modelId;
    this.#models.set(modelId, group);
    this.#streamBuilder = null;
    this.#refitScene();
  }

  // ── Viewer commands ──

  fitAll() {
    this.#refitScene();
  }

  setProjection(orthographic) {
    const parent = this.#canvas.parentElement;
    const w = parent.clientWidth;
    const h = parent.clientHeight;
    const aspect = w / h;

    const oldPos = this.#camera.position.clone();
    const target = this.#controls.target.clone();

    if (orthographic && this.#camera.isPerspectiveCamera) {
      const dist = oldPos.distanceTo(target);
      const halfH = dist * Math.tan(THREE.MathUtils.degToRad(this.#camera.fov / 2));
      const cam = new THREE.OrthographicCamera(
        -halfH * aspect, halfH * aspect, halfH, -halfH,
        this.#camera.near, this.#camera.far);
      cam.position.copy(oldPos);
      cam.quaternion.copy(this.#camera.quaternion);
      cam.zoom = 1;
      cam.updateProjectionMatrix();
      this.#camera = cam;
      this.#controls.object = cam;
      this.#controls.update();
    } else if (!orthographic && this.#camera.isOrthographicCamera) {
      const halfH = (this.#camera.top - this.#camera.bottom) / (2 * this.#camera.zoom);
      const fov = 50;
      const dist = halfH / Math.tan(THREE.MathUtils.degToRad(fov / 2));
      const dir = oldPos.clone().sub(target).normalize();
      const cam = new THREE.PerspectiveCamera(fov, aspect, this.#camera.near, this.#camera.far);
      cam.position.copy(target).addScaledVector(dir, dist);
      cam.quaternion.copy(this.#camera.quaternion);
      cam.updateProjectionMatrix();
      this.#camera = cam;
      this.#controls.object = cam;
      this.#controls.update();
    }
  }

  getProjection() {
    return this.#camera.isOrthographicCamera ? "orthographic" : "perspective";
  }

  setBackgroundColor(color) {
    if (this.#scene) {
      this.#scene.background = new THREE.Color(color);
    }
  }

  dispose() {
    this.#renderer?.setAnimationLoop(null);
    this.#resizeObserver?.disconnect();
    this.clearScene();
    this.#renderer?.dispose();
  }

  #animate() {
    this.#controls?.update();
    this.#renderer?.render(this.#scene, this.#camera);
  }

  #refitScene() {
    const box = new THREE.Box3();
    for (const group of this.#models.values()) {
      if (group.visible) box.expandByObject(group);
    }
    if (box.isEmpty()) return;
    this.#removeGrid();
    this.addGrid(box);
    this.fitToBox(box);
  }

  #removeGrid() {
    const grid = this.#scene.getObjectByName("ground-grid");
    if (grid) {
      grid.geometry?.dispose();
      this.#scene.remove(grid);
    }
  }

  #disposeGroup(group) {
    group.traverse(obj => {
      if (obj.isMesh || obj.isInstancedMesh) {
        obj.geometry?.dispose();
        if (Array.isArray(obj.material)) obj.material.forEach(m => m.dispose());
        else obj.material?.dispose();
      }
    });
  }
}
