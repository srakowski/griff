// The page's side of Griff's web player: draws the frames the game builds (see Griff.Web's WebFrame) with WebGL 2, runs
// a frame each time the browser shows one, and sends the game the keyboard and mouse. The C# side calls the exported
// functions here (its [JSImport]s); `attach` gives this side the C# WebPlayer's [JSExport]s.

const canvas = document.getElementById('game');
const status = document.getElementById('status');

// A quad's floats and bytes (its corners in clip space, the part of the image, tint and flash: four floats each).
const QUAD_FLOATS = 20;
const QUAD_BYTES = QUAD_FLOATS * 4;
// The image id of a batch of holes (a scene change's iris), drawn with the shape shader; 0 is no image (white), and
// -2, -3... are the glyph atlas's pages (see glyphs).
const HOLE = -1;

// The quads and holes are drawn as the desktop renderer draws them (Griff.Graphics' SpriteRenderer), in GLSL ES.
const VERTEX = `#version 300 es
layout(location = 0) in vec4 aCorners01;
layout(location = 1) in vec4 aCorners23;
layout(location = 2) in vec4 aSource;
layout(location = 3) in vec4 aColor;
layout(location = 4) in vec4 aFlash;
out vec2 vUv;
out vec4 vColor;
out vec4 vFlash;
void main()
{
    vec2 position;
    vec2 uv;
    if (gl_VertexID == 0) { position = aCorners01.zw; uv = vec2(1.0, 0.0); }
    else if (gl_VertexID == 1) { position = aCorners23.xy; uv = vec2(1.0, 1.0); }
    else if (gl_VertexID == 2) { position = aCorners01.xy; uv = vec2(0.0, 0.0); }
    else { position = aCorners23.zw; uv = vec2(0.0, 1.0); }
    vUv = aSource.xy + uv * aSource.zw;
    vColor = aColor;
    vFlash = aFlash;
    gl_Position = vec4(position, 0.0, 1.0);
}`;

const SPRITE = `#version 300 es
precision mediump float;
in vec2 vUv;
in vec4 vColor;
in vec4 vFlash;
uniform sampler2D uTexture;
out vec4 fragColor;
void main()
{
    vec4 color = texture(uTexture, vUv) * vColor;
    fragColor = vec4(mix(color.rgb, vFlash.rgb, vFlash.a), color.a);
}`;

// The color everywhere outside a circle of the flash's first channel (a fraction of the quad's half-width).
const HOLE_SHAPE = `#version 300 es
precision mediump float;
in vec2 vUv;
in vec4 vColor;
in vec4 vFlash;
out vec4 fragColor;
void main()
{
    float d = length(vUv * 2.0 - 1.0);
    float edge = fwidth(d);
    fragColor = vec4(vColor.rgb, vColor.a * smoothstep(vFlash.x - edge, vFlash.x, d));
}`;

let player = null;
let gl = null;
let spriteProgram = null;
let holeProgram = null;
let vao = null;
let buffer = null;
let white = null;
const textures = new Map();
let quadBytes = new Uint8Array(QUAD_BYTES * 1024);

// What a test (or a person in the console) can see of the game: 'loading', 'running' or 'failed: <why>', and the frames drawn.
globalThis.griff = { status: 'loading', frames: 0 };

/** Takes the C# WebPlayer's exports (called by main.js before the game starts). */
export function attach(exports) {
  player = exports;
}

/** The page's address, which the game's files are fetched relative to. */
export function baseUrl() {
  return document.baseURI;
}

/** Shows what went wrong over the game, and stops its frames. */
export function fail(message) {
  status.hidden = false;
  status.textContent = message;
  status.className = 'failed';
  globalThis.griff.status = `failed: ${message}`;
}

/** Starts drawing frames and listening to the keyboard and mouse. */
export function start(title) {
  document.title = title;
  gl = canvas.getContext('webgl2', { alpha: false, antialias: false, depth: false, stencil: false });
  if (!gl) {
    fail("This browser can't show the game: it has no WebGL 2.");
    return;
  }

  setUp();
  listen();
  status.hidden = true;
  globalThis.griff.status = 'running';
  canvas.focus();
  requestAnimationFrame(frame);
}

/** Gives an image (a file's bytes; none for a missing one) a texture under `id`. */
export function loadImage(id, file) {
  // The view lasts only for this call, so it's copied before decoding (which finishes later).
  const bytes = file.slice();
  const texture = makeTexture(new Uint8Array([0, 0, 0, 0]), 1, 1);
  textures.set(id, texture);
  createImageBitmap(new Blob([bytes]), { premultiplyAlpha: 'none', colorSpaceConversion: 'none' })
    .then(image => upload(texture, image))
    .catch(() => upload(texture, new Uint8Array([255, 0, 255, 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 0, 255, 255]), 2, 2));
}

/**
 * Copies `rows` rows from `top` of a glyph atlas page (RGBA, `size` pixels wide) to its texture under `id`, making it
 * `size` pixels square the first time. Text is smoothed, as on the desktop.
 */
export function glyphs(id, size, top, rows, rgba) {
  let texture = textures.get(id);
  if (!texture) {
    texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, size, size, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    textures.set(id, texture);
  }

  gl.bindTexture(gl.TEXTURE_2D, texture);
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
  gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
  gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL, gl.NONE);
  gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, top, size, rows, gl.RGBA, gl.UNSIGNED_BYTE, rgba.slice());
}

/** Draws a frame: the quads (as bytes), the batches (image id, quad count) and the background color. */
export function draw(quads, batches, red, green, blue) {
  const length = quads.byteLength;
  if (quadBytes.byteLength < length)
    quadBytes = new Uint8Array(Math.max(length, quadBytes.byteLength * 2));
  quads.copyTo(quadBytes.subarray(0, length));
  const runs = batches.slice();

  gl.viewport(0, 0, canvas.width, canvas.height);
  gl.clearColor(red, green, blue, 1);
  gl.clear(gl.COLOR_BUFFER_BIT);
  gl.enable(gl.BLEND);
  gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
  gl.bindVertexArray(vao);
  gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
  gl.bufferData(gl.ARRAY_BUFFER, quadBytes.subarray(0, length), gl.STREAM_DRAW);
  gl.activeTexture(gl.TEXTURE0);

  // Each batch reads its quads from where they start in the buffer (WebGL 2 has no base instance).
  let first = 0;
  for (let i = 0; i < runs.length; i += 2) {
    const [image, count] = [runs[i], runs[i + 1]];
    if (image === HOLE) {
      gl.useProgram(holeProgram);
    } else {
      gl.useProgram(spriteProgram);
      gl.bindTexture(gl.TEXTURE_2D, textures.get(image) ?? white);
    }

    for (let attribute = 0; attribute < 5; attribute++)
      gl.vertexAttribPointer(attribute, 4, gl.FLOAT, false, QUAD_BYTES, first * QUAD_BYTES + attribute * 16);
    gl.drawArraysInstanced(gl.TRIANGLE_STRIP, 0, 4, count);
    first += count;
  }

  gl.bindVertexArray(null);
}

function frame(now) {
  if (globalThis.griff.status !== 'running')
    return;
  const scale = devicePixelRatio || 1;
  const width = Math.max(1, Math.round(canvas.clientWidth * scale));
  const height = Math.max(1, Math.round(canvas.clientHeight * scale));
  if (canvas.width !== width || canvas.height !== height)
    [canvas.width, canvas.height] = [width, height];
  player.Frame(now, width, height);
  globalThis.griff.frames++;
  requestAnimationFrame(frame);
}

function setUp() {
  spriteProgram = program(SPRITE);
  gl.useProgram(spriteProgram);
  gl.uniform1i(gl.getUniformLocation(spriteProgram, 'uTexture'), 0);
  holeProgram = program(HOLE_SHAPE);

  // Every attribute is the instance's: a quad's four corners all read the same one.
  vao = gl.createVertexArray();
  gl.bindVertexArray(vao);
  buffer = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
  for (let attribute = 0; attribute < 5; attribute++) {
    gl.enableVertexAttribArray(attribute);
    gl.vertexAttribDivisor(attribute, 1);
  }

  gl.bindVertexArray(null);
  white = makeTexture(new Uint8Array([255, 255, 255, 255]), 1, 1);
}

function program(fragmentSource) {
  const compile = (type, source) => {
    const shader = gl.createShader(type);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS))
      throw new Error(`A shader failed to compile: ${gl.getShaderInfoLog(shader)}`);
    return shader;
  };
  const linked = gl.createProgram();
  gl.attachShader(linked, compile(gl.VERTEX_SHADER, VERTEX));
  gl.attachShader(linked, compile(gl.FRAGMENT_SHADER, fragmentSource));
  gl.linkProgram(linked);
  if (!gl.getProgramParameter(linked, gl.LINK_STATUS))
    throw new Error(`A shader failed to link: ${gl.getProgramInfoLog(linked)}`);
  return linked;
}

function makeTexture(rgba, width, height) {
  const texture = gl.createTexture();
  upload(texture, rgba, width, height);
  return texture;
}

// Pixel art is the common jam style, so images sample with nearest-neighbour filtering, as on the desktop.
function upload(texture, source, width, height) {
  gl.bindTexture(gl.TEXTURE_2D, texture);
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
  gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
  gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL, gl.NONE);
  if (source instanceof Uint8Array)
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, source);
  else
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, source);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
}

// Keys are the game's by where they are (KeyboardEvent.code), as on the desktop. The page keeps the ones the game takes,
// so the arrows and Space don't scroll it, but leaves the browser its own: F keys and shortcuts with Ctrl or Cmd.
function listen() {
  const keep = e => !e.ctrlKey && !e.metaKey && !/^F\d+$/.test(e.code);
  addEventListener('keydown', e => {
    if (player.Key(e.code, true, e.repeat) && keep(e))
      e.preventDefault();
    if (e.key === 'Backspace')
      player.Type('\b');
    else if (e.key.length === 1 && !e.ctrlKey && !e.metaKey)
      player.Type(e.key);
  });
  addEventListener('keyup', e => {
    if (player.Key(e.code, false, false) && keep(e))
      e.preventDefault();
  });
  addEventListener('blur', () => player.Blur());

  canvas.addEventListener('pointermove', e => {
    const box = canvas.getBoundingClientRect();
    const scale = devicePixelRatio || 1;
    player.Pointer((e.clientX - box.left) / box.width, (e.clientY - box.top) / box.height, e.movementX * scale, e.movementY * scale);
  });
  canvas.addEventListener('pointerleave', () => player.PointerLeft());
  canvas.addEventListener('pointerdown', e => {
    canvas.focus();
    canvas.setPointerCapture(e.pointerId);
    player.Button(e.button, true);
  });
  canvas.addEventListener('pointerup', e => player.Button(e.button, false));
  canvas.addEventListener('contextmenu', e => e.preventDefault());
  // A notch is 100 pixels of scrolling in Chrome and Edge, or 3 lines; more than 0 is away from the player, as on Windows.
  canvas.addEventListener('wheel', e => {
    player.Wheel(-e.deltaY / (e.deltaMode === 0 ? 100 : e.deltaMode === 1 ? 3 : 1));
    e.preventDefault();
  }, { passive: false });
}
