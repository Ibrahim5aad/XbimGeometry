import * as THREE from "three/webgpu";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";
import { WexBIMLoader } from "./wexBimLoader.js";

let renderer, scene, camera, controls, canvas, resizeObserver;

async function init(canvasId) {
  canvas = document.getElementById(canvasId);
  if (!canvas) return;

  renderer = new THREE.WebGPURenderer({ canvas, antialias: true });
  renderer.setPixelRatio(window.devicePixelRatio);
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.2;

  await renderer.init();

  scene = new THREE.Scene();
  scene.background = new THREE.Color(0x0a0a1a);

  camera = new THREE.PerspectiveCamera(50, 1, 0.1, 100000);

  controls = new OrbitControls(camera, canvas);
  controls.enableDamping = true;
  controls.dampingFactor = 0.1;

  // Lighting
  scene.add(new THREE.AmbientLight(0xffffff, 0.5));
  const dir1 = new THREE.DirectionalLight(0xffffff, 0.9);
  dir1.position.set(1, 2, 1.5).normalize();
  scene.add(dir1);
  const dir2 = new THREE.DirectionalLight(0xffffff, 0.3);
  dir2.position.set(-1, -0.5, -1).normalize();
  scene.add(dir2);

  // Watch the viewport container for size changes (sidebar appearing, window resize, etc.)
  resizeObserver = new ResizeObserver(() => resize());
  resizeObserver.observe(canvas.parentElement);

  resize();
  renderer.setAnimationLoop(animate);
}

function resize() {
  if (!canvas || !renderer) return;
  const parent = canvas.parentElement;
  const w = parent.clientWidth;
  const h = parent.clientHeight;
  if (w === 0 || h === 0) return;
  renderer.setSize(w, h, false);
  canvas.style.width = w + "px";
  canvas.style.height = h + "px";
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
}

function animate() {
  controls?.update();
  renderer?.render(scene, camera);
}

function fitCamera(group) {
  const box = new THREE.Box3().setFromObject(group);
  if (box.isEmpty()) return;
  const center = box.getCenter(new THREE.Vector3());
  const size = box.getSize(new THREE.Vector3());
  const maxDim = Math.max(size.x, size.y, size.z);
  const dist = maxDim * 1.5;

  camera.position.set(center.x + dist * 0.7, center.y + dist * 0.5, center.z + dist * 0.7);
  camera.lookAt(center);
  camera.near = Math.max(0.01, maxDim * 0.001);
  camera.far = maxDim * 100;
  camera.updateProjectionMatrix();

  controls.target.copy(center);
  controls.minDistance = maxDim * 0.05;
  controls.maxDistance = maxDim * 10;
  controls.update();
}

window.xbimViewer = {
  async init(canvasId) {
    await init(canvasId);
  },

  async loadWexBim(streamRef) {
    if (!renderer) return;

    // Remove previous model geometry and grid (keep lights)
    const toRemove = [];
    scene.traverse((obj) => {
      if (obj.isMesh || obj.isInstancedMesh || obj.name === "ground-grid") toRemove.push(obj);
    });
    toRemove.forEach((obj) => {
      obj.geometry?.dispose();
      obj.material?.dispose();
      obj.parent?.remove(obj);
    });

    const buffer = await streamRef.arrayBuffer();
    const loader = new WexBIMLoader();
    const group = loader.parse(buffer);
    scene.add(group);

    // Fit grid to model bounds
    const box = new THREE.Box3().setFromObject(group);
    if (!box.isEmpty()) {
      const size = box.getSize(new THREE.Vector3());
      const center = box.getCenter(new THREE.Vector3());
      const gridSize = Math.max(size.x, size.z) * 2;
      const grid = new THREE.GridHelper(gridSize, 40, 0x1a2744, 0x111b2e);
      grid.name = "ground-grid";
      grid.position.set(center.x, box.min.y, center.z);
      scene.add(grid);
    }

    fitCamera(group);
  },
};
