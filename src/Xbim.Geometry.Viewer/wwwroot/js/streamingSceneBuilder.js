import * as THREE from "three/webgpu";

/**
 * Receives streamed geometry and product instances from the C# scene pipeline
 * and builds a Three.js scene progressively. Products appear in the viewport
 * as they arrive rather than waiting for the entire model.
 */
export class StreamingSceneBuilder {
  constructor(scene, modelId) {
    this._scene = scene;
    this._modelId = modelId;
    this._group = new THREE.Group();
    this._group.userData.modelId = modelId;
    this._geometries = new Map();
    this._materials = new Map();
    this._bounds = new THREE.Box3();
    this._instanceCount = 0;

    // Y-up <-> Z-up swap (IFC is Z-up, Three.js is Y-up)
    this._coordSwap = new THREE.Matrix4().set(
      1, 0, 0, 0,
      0, 0, 1, 0,
      0, 1, 0, 0,
      0, 0, 0, 1
    );
    this._coordSwapInv = this._coordSwap.clone().invert();

    scene.add(this._group);
  }

  get modelId() { return this._modelId; }
  get group() { return this._group; }
  get instanceCount() { return this._instanceCount; }
  get bounds() { return this._bounds; }

  setHeader(oneMeter, bx, by, bz, sx, sy, sz, productCount) {
    this._estimatedProductCount = productCount;
  }

  addStyle(id, r, g, b, a) {
    const color = new THREE.Color(r, g, b);
    const isTransparent = a < 0.99;
    const mat = new THREE.MeshStandardMaterial({
      color,
      transparent: isTransparent,
      opacity: a,
      depthWrite: !isTransparent,
      side: THREE.DoubleSide,
      roughness: 0.7,
      metalness: 0.1,
      flatShading: true,
      emissive: color.clone().multiplyScalar(0.1),
    });
    this._materials.set(id, mat);
  }

  addGeometry(id, meshDataBuffer) {
    const geom = this._parsePolyhedronBinary(meshDataBuffer);
    if (geom) this._geometries.set(id, geom);
  }

  addInstance(productLabel, typeId, geometryId, styleId, transformArray, bx, by, bz, sx, sy, sz) {
    const geom = this._geometries.get(geometryId);
    if (!geom) return;

    const mat = this._materials.get(styleId) || this._defaultMaterial();
    const mesh = new THREE.Mesh(geom, mat);

    if (transformArray && transformArray.length === 16) {
      const m = new THREE.Matrix4().fromArray(transformArray);
      const swapped = new THREE.Matrix4()
        .multiplyMatrices(this._coordSwap, m)
        .multiply(this._coordSwapInv);
      mesh.applyMatrix4(swapped);
    }

    mesh.userData.productLabel = productLabel;
    mesh.userData.typeId = typeId;
    if (mat.transparent) mesh.renderOrder = 1;
    this._group.add(mesh);
    this._instanceCount++;

    const meshBox = new THREE.Box3().setFromObject(mesh);
    this._bounds.union(meshBox);
  }

  complete() {
    // Future: convert repeated geometries to InstancedMesh
  }

  dispose() {
    this._geometries.forEach(g => g.dispose());
    this._geometries.clear();
    this._materials.forEach(m => m.dispose());
    this._materials.clear();
    if (this._defaultMat) this._defaultMat.dispose();
  }

  _defaultMaterial() {
    if (!this._defaultMat) {
      this._defaultMat = new THREE.MeshStandardMaterial({
        color: 0xcccccc,
        side: THREE.DoubleSide,
        roughness: 0.7,
        metalness: 0.1,
        flatShading: true,
      });
    }
    return this._defaultMat;
  }

  _parsePolyhedronBinary(arrayBuffer) {
    const view = new DataView(arrayBuffer);
    let offset = 0;

    const read = {
      byte: () => { const v = view.getUint8(offset); offset += 1; return v; },
      int32: () => { const v = view.getInt32(offset, true); offset += 4; return v; },
      uint16: () => { const v = view.getUint16(offset, true); offset += 2; return v; },
      float32: () => { const v = view.getFloat32(offset, true); offset += 4; return v; },
    };

    read.byte(); // version
    const numVerts = read.int32();
    const numTris = read.int32();

    if (numVerts <= 0 || numTris <= 0) return null;

    const vertices = new Float32Array(numVerts * 3);
    const indices = new Uint32Array(numTris * 3);
    const normals = new Float32Array(numVerts * 3);
    const normalCounts = new Uint32Array(numVerts);

    for (let i = 0; i < numVerts; i++) {
      const x = read.float32(), y = read.float32(), z = read.float32();
      vertices[i * 3] = x;
      vertices[i * 3 + 1] = z;
      vertices[i * 3 + 2] = y;
    }

    let readIdx;
    if (numVerts <= 0xFF) readIdx = read.byte;
    else if (numVerts <= 0xFFFF) readIdx = read.uint16;
    else readIdx = read.int32;

    const numFaces = read.int32();
    let iIdx = 0;

    for (let f = 0; f < numFaces; f++) {
      let numFaceTris = read.int32();
      if (numFaceTris === 0) continue;
      const isPlanar = numFaceTris > 0;
      numFaceTris = Math.abs(numFaceTris);

      if (isPlanar) {
        const u = read.byte(), v = read.byte();
        const n = this._decodeNormal(u, v);
        for (let j = 0; j < numFaceTris * 3; j++) {
          const vi = readIdx();
          indices[iIdx++] = vi;
          const off = vi * 3;
          normals[off] += n.x; normals[off + 1] += n.y; normals[off + 2] += n.z;
          normalCounts[vi]++;
        }
      } else {
        for (let j = 0; j < numFaceTris; j++) {
          for (let k = 0; k < 3; k++) {
            const vi = readIdx();
            indices[iIdx++] = vi;
            const u = read.byte(), v = read.byte();
            const n = this._decodeNormal(u, v);
            const off = vi * 3;
            normals[off] += n.x; normals[off + 1] += n.y; normals[off + 2] += n.z;
            normalCounts[vi]++;
          }
        }
      }
    }

    for (let i = 0; i < numVerts; i++) {
      if (normalCounts[i] > 0) {
        const off = i * 3;
        normals[off] /= normalCounts[i];
        normals[off + 1] /= normalCounts[i];
        normals[off + 2] /= normalCounts[i];
        const len = Math.sqrt(normals[off] ** 2 + normals[off + 1] ** 2 + normals[off + 2] ** 2);
        if (len > 0) { normals[off] /= len; normals[off + 1] /= len; normals[off + 2] /= len; }
      }
    }

    const geom = new THREE.BufferGeometry();
    geom.setAttribute("position", new THREE.BufferAttribute(vertices, 3));
    geom.setIndex(new THREE.BufferAttribute(indices, 1));
    geom.setAttribute("normal", new THREE.BufferAttribute(normals, 3));
    geom.computeBoundingSphere();
    return geom;
  }

  _decodeNormal(u, v) {
    const un = 2 * (u / 255) - 1;
    const vn = 2 * (v / 255) - 1;
    const zSq = 1 - un * un - vn * vn;
    const nz = zSq > 0 ? Math.sqrt(zSq) : 0;
    return { x: un, y: -nz, z: vn };
  }
}
