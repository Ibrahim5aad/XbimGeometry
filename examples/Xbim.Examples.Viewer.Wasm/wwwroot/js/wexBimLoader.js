// WexBIM Loader for Three.js
// Adapted from https://github.com/Ibrahim5aad/wex-threejs
import * as THREE from "three";

class BinaryReader {
  constructor(arrayBuffer) {
    this.view = new DataView(arrayBuffer);
    this.offset = 0;
  }
  readInt32() { const v = this.view.getInt32(this.offset, true); this.offset += 4; return v; }
  readUint16() { const v = this.view.getUint16(this.offset, true); this.offset += 2; return v; }
  readInt16() { const v = this.view.getInt16(this.offset, true); this.offset += 2; return v; }
  readByte() { const v = this.view.getUint8(this.offset); this.offset += 1; return v; }
  readFloat32() { const v = this.view.getFloat32(this.offset, true); this.offset += 4; return v; }
  readFloat64() { const v = this.view.getFloat64(this.offset, true); this.offset += 8; return v; }
  readFloat32Array(count) { const a = new Float32Array(count); for (let i = 0; i < count; i++) a[i] = this.readFloat32(); return a; }
  readFloat64Array(count) { const a = new Float64Array(count); for (let i = 0; i < count; i++) a[i] = this.readFloat64(); return a; }
  getSubReader(length) {
    const sub = new BinaryReader(this.view.buffer.slice(this.offset, this.offset + length));
    this.offset += length;
    return sub;
  }
  isEOF() { return this.offset >= this.view.byteLength; }
}

class StyleMap {
  constructor() {
    this.styles = {};
    this.materialCache = {};
  }

  add(record) { this.styles[record.id] = record; }
  get(id) { return this.styles[id] || null; }

  getMaterial(styleId) {
    if (this.materialCache[styleId]) return this.materialCache[styleId];
    const style = this.get(styleId);
    if (!style) return this._defaultMaterial();

    const color = style.color instanceof THREE.Color ? style.color : new THREE.Color(0.7, 0.7, 0.7);
    const mat = new THREE.MeshStandardMaterial({
      color,
      transparent: style.transparent || false,
      opacity: style.opacity || 1.0,
      side: THREE.DoubleSide,
      roughness: 0.7,
      metalness: 0.1,
      flatShading: true,
      emissive: color.clone().multiplyScalar(0.1),
    });
    this.materialCache[styleId] = mat;
    return mat;
  }

  _defaultMaterial() {
    if (!this.materialCache["__default"]) {
      this.materialCache["__default"] = new THREE.MeshStandardMaterial({
        color: 0xcccccc, side: THREE.DoubleSide, roughness: 0.7, metalness: 0.1, flatShading: true,
      });
    }
    return this.materialCache["__default"];
  }
}

export class WexBIMLoader {
  constructor() {
    this.productMaps = {};
    this.regions = [];
    this._styleMap = new StyleMap();
    this._coordTransform = new THREE.Matrix4().set(
      1, 0, 0, 0,
      0, 0, 1, 0,
      0, 1, 0, 0,
      0, 0, 0, 1
    );
  }

  parse(arrayBuffer) {
    const reader = new BinaryReader(arrayBuffer);
    const magic = reader.readInt32();
    if (magic !== 94132117) throw new Error("Invalid WexBIM file.");

    const version = reader.readByte();
    const numShapes = reader.readInt32();
    reader.readInt32(); // numVertices
    reader.readInt32(); // numTriangles
    reader.readInt32(); // numMatrices
    const numProducts = reader.readInt32();
    const numStyles = reader.readInt32();
    reader.readFloat32(); // meter
    if (version > 3) { reader.readFloat64(); reader.readFloat64(); reader.readFloat64(); }
    const numRegions = reader.readInt16();

    this.regions = this._parseRegions(reader, numRegions);
    this._parseStyles(reader, numStyles);
    this._parseProducts(reader, numProducts);

    const scene = new THREE.Group();

    if (version >= 3) {
      for (let r = 0; r < numRegions; r++) {
        const geomCount = reader.readInt32();
        for (let g = 0; g < geomCount; g++) {
          const shapes = this._parseShape(reader, version);
          const geomLen = reader.readInt32();
          if (geomLen === 0) continue;
          const gbr = reader.getSubReader(geomLen);
          const geomData = this._parseGeometry(gbr);
          const geometry = this._createBufferGeometry(geomData);
          if (geometry) this._addToScene(scene, shapes, geometry);
        }
      }
    } else {
      // v1/v2: flat shape list, triangulation is inline (no byte-length prefix)
      for (let i = 0; i < numShapes; i++) {
        const shapes = this._parseShape(reader, version);
        const geomData = this._parseGeometry(reader);
        const geometry = this._createBufferGeometry(geomData);
        if (geometry) this._addToScene(scene, shapes, geometry);
      }
    }

    return scene;
  }

  _parseRegions(reader, count) {
    const regions = [];
    for (let i = 0; i < count; i++) {
      const population = reader.readInt32();
      const c = reader.readFloat32Array(3);
      const bb = reader.readFloat32Array(6);
      regions.push({
        population,
        center: [c[0], c[2], c[1]],
        bbox: [bb[0], bb[2], bb[1], bb[3], bb[5], bb[4]],
      });
    }
    return regions;
  }

  _parseStyles(reader, count) {
    for (let i = 0; i < count; i++) {
      const id = reader.readInt32();
      const r = reader.readFloat32(), g = reader.readFloat32(), b = reader.readFloat32(), a = reader.readFloat32();
      this._styleMap.add({
        id, index: i, transparent: a < 0.99, opacity: a, color: new THREE.Color(r, g, b),
      });
    }
    this._styleMap.add({ id: -1, index: count, transparent: false, opacity: 1, color: new THREE.Color(1, 0, 0) });
    this._styleMap.add({ id: -2, index: count + 1, transparent: false, opacity: 1, color: new THREE.Color(0, 0, 1) });
  }

  _parseProducts(reader, count) {
    for (let i = 0; i < count; i++) {
      const label = reader.readInt32();
      const type = reader.readInt16();
      reader.readFloat32Array(6); // bbox
      this.productMaps[label] = { type };
    }
  }

  _parseShape(reader, version) {
    const repetition = reader.readInt32();
    const shapes = [];
    for (let i = 0; i < repetition; i++) {
      const pLabel = reader.readInt32();
      reader.readInt16(); // instanceTypeId
      reader.readInt32(); // instanceLabel
      const styleId = reader.readInt32();

      let transform = null;
      if (repetition > 1) {
        const raw = version === 1 ? reader.readFloat32Array(16) : reader.readFloat64Array(16);
        transform = this._transformMatrix(raw);
      }

      const type = this.productMaps[pLabel]?.type || 0;
      const finalStyleId = (type === 3 || type === 4) ? -2 : styleId;
      const style = this._styleMap.get(finalStyleId) || this._styleMap.get(-1);

      shapes.push({
        styleId: finalStyleId,
        transparent: style.transparent,
        opacity: style.opacity || 1,
        transform,
      });
    }
    return shapes;
  }

  _parseGeometry(reader) {
    reader.readByte(); // version
    const numVerts = reader.readInt32();
    const numTris = reader.readInt32();

    if (numVerts <= 0 || numTris <= 0) {
      // Skip remaining vertex data if present
      for (let i = 0; i < numVerts * 3; i++) reader.readFloat32();
      reader.readInt32(); // faceCount
      return { vertices: new Float32Array(0), indices: new Uint32Array(0), normals: new Float32Array(0) };
    }

    const vertices = new Float32Array(numVerts * 3);
    const indices = new Uint32Array(numTris * 3);
    const normals = new Float32Array(numVerts * 3);
    const normalCounts = new Uint32Array(numVerts);

    for (let i = 0; i < numVerts; i++) {
      const x = reader.readFloat32(), y = reader.readFloat32(), z = reader.readFloat32();
      vertices[i * 3] = x;
      vertices[i * 3 + 1] = z;
      vertices[i * 3 + 2] = y;
    }

    let readIdx;
    if (numVerts <= 0xFF) readIdx = () => reader.readByte();
    else if (numVerts <= 0xFFFF) readIdx = () => reader.readUint16();
    else readIdx = () => reader.readInt32();

    const numFaces = reader.readInt32();
    let iIdx = 0;

    for (let f = 0; f < numFaces; f++) {
      let numFaceTris = reader.readInt32();
      if (numFaceTris === 0) continue;
      const isPlanar = numFaceTris > 0;
      numFaceTris = Math.abs(numFaceTris);

      if (isPlanar) {
        const u = reader.readByte(), v = reader.readByte();
        const n = this._decodeNormal(u, v);
        const nx = n.x, ny = n.z, nz = n.y;
        for (let j = 0; j < numFaceTris * 3; j++) {
          const vi = readIdx();
          indices[iIdx++] = vi;
          const off = vi * 3;
          normals[off] += nx; normals[off + 1] += ny; normals[off + 2] += nz;
          normalCounts[vi]++;
        }
      } else {
        for (let j = 0; j < numFaceTris; j++) {
          for (let k = 0; k < 3; k++) {
            const vi = readIdx();
            indices[iIdx++] = vi;
            const u = reader.readByte(), v = reader.readByte();
            const n = this._decodeNormal(u, v);
            const off = vi * 3;
            normals[off] += n.x; normals[off + 1] += n.z; normals[off + 2] += n.y;
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

    return { vertices, indices, normals };
  }

  _decodeNormal(u, v) {
    const un = 2 * (u / 255) - 1;
    const vn = 2 * (v / 255) - 1;
    const zSq = 1 - un * un - vn * vn;
    const n = new THREE.Vector3(un, vn, zSq > 0 ? Math.sqrt(zSq) : 0);
    n.normalize();
    n.z = -n.z;
    return n;
  }

  _createBufferGeometry(data) {
    if (!data || !data.vertices.length || !data.indices.length) return null;
    const geom = new THREE.BufferGeometry();
    geom.setAttribute("position", new THREE.BufferAttribute(data.vertices, 3));
    geom.setIndex(new THREE.BufferAttribute(data.indices, 1));
    if (data.normals.length > 0) {
      geom.setAttribute("normal", new THREE.BufferAttribute(data.normals, 3));
    } else {
      geom.computeVertexNormals();
    }
    geom.computeBoundingSphere();
    return geom;
  }

  _addToScene(scene, shapes, geometry) {
    if (shapes.length === 1) {
      const s = shapes[0];
      const mat = this._styleMap.getMaterial(s.styleId);
      const mesh = new THREE.Mesh(geometry, mat);
      if (s.transform) mesh.applyMatrix4(new THREE.Matrix4().fromArray(s.transform));
      scene.add(mesh);
    } else {
      const byStyle = {};
      shapes.forEach(s => { (byStyle[s.styleId] ??= []).push(s); });
      for (const sid in byStyle) {
        const group = byStyle[sid];
        const mat = this._styleMap.getMaterial(Number(sid));
        const inst = new THREE.InstancedMesh(geometry, mat, group.length);
        const m = new THREE.Matrix4();
        group.forEach((s, i) => {
          s.transform ? m.fromArray(s.transform) : m.identity();
          inst.setMatrixAt(i, m);
        });
        inst.instanceMatrix.needsUpdate = true;
        scene.add(inst);
      }
    }
  }

  _transformMatrix(raw) {
    if (!raw) return null;
    const m = new THREE.Matrix4().fromArray(raw);
    return new THREE.Matrix4()
      .multiplyMatrices(this._coordTransform, m)
      .multiply(new THREE.Matrix4().copy(this._coordTransform).invert())
      .elements;
  }
}
