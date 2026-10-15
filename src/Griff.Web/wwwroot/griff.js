// The page's side of Griff's web player: draws the frames the game builds with WebGL 2, runs a frame each time the
// browser shows one, and sends the game the keyboard and mouse. The C# side calls the exported functions here (its
// [JSImport]s); `attach` gives this side the C# WebPlayer's [JSExport]s.
//
// A frame comes as a draw list (Griff.Graphics' DrawList, the same one the desktop's OpenGL renderer draws): its quads,
// its commands (five ints each: the op, a texture, the first quad, how many, and where its values start) and its values
// (colors, a view's place, the screen effects' settings). `draw` runs the commands as OpenGLRenderer does, with the same
// shaders (given by `start`), so both draw the same pixels.

const canvas = document.getElementById('game');
const status = document.getElementById('status');

// A quad's floats and bytes (its corners in clip space, the part of the texture, tint and flash: four floats each).
const QUAD_FLOATS = 20;
const QUAD_BYTES = QUAD_FLOATS * 4;
const COMMAND_INTS = 5;

// The draw list's ops (DrawOp), the picture target (DrawTarget) and the screen effects' settings (LookValue), by number.
const TARGET = 0, VIEW = 1, CLEAR = 2, SPRITES = 3, HOLES = 4, BEGIN_LIGHT = 5, LIGHTS = 6, SHADOWS = 7, SHADOWED_LIGHTS = 8,
  END_LIGHT = 9, EFFECTS = 10;
const PICTURE = 1;
const LOOK = ['uVignette', 'uScanlines', 'uCurvature', 'uPixel', 'uAberration', 'uSaturation', 'uContrast', 'uBrightness'];
const LOOK_TINT = 8, LOOK_GRAIN = 11, LOOK_PALETTE = 12, LOOK_TIME = 13;

let player = null;
let gl = null;
let spriteProgram = null;
let shapeProgram = null;
let shapeMode = null;
let effectsProgram = null;
let effects = null;
let vao = null;
let buffer = null;
let wholeVao = null;
let wholeBuffer = null;
let white = null;
let missing = null;
let picture = null;
let light = null;
const textures = new Map();
const whole = new Float32Array(QUAD_FLOATS);
let quadBytes = new Uint8Array(QUAD_BYTES * 1024);
let capturing = null;

// What a test (or a person in the console) can see of the game: 'loading', 'running' or 'failed: <why>', the frames
// drawn, the images still being decoded, and capture(), which resolves to the next frame's pixels (top-down RGBA, in
// base64) once it's drawn.
globalThis.griff = {
  status: 'loading',
  frames: 0,
  pending: 0,
  capture: () => new Promise(resolve => { capturing = resolve; }),
};

/** Takes the C# WebPlayer's exports (called by main.js before the game starts). */
export function attach(exports) {
  player = exports;
}

/** The page's address, which the game's files are fetched relative to. */
export function baseUrl() {
  return document.baseURI;
}

/** The page address's query, with its '?' ('' for none). */
export function query() {
  return location.search;
}

/** Shows what went wrong over the game, and stops its frames. */
export function fail(message) {
  status.hidden = false;
  status.textContent = message;
  status.className = 'failed';
  globalThis.griff.status = `failed: ${message}`;
}

/** Starts drawing frames, with these shaders (`header` goes before each), and listening to the keyboard and mouse. */
export function start(title, header, vertex, sprite, shape, effectsSource) {
  document.title = title;
  gl = canvas.getContext('webgl2', { alpha: false, antialias: false, depth: false, stencil: false });
  if (!gl) {
    fail("This browser can't show the game: it has no WebGL 2.");
    return;
  }

  setUp(header, vertex, sprite, shape, effectsSource);
  listen();
  status.hidden = true;
  globalThis.griff.status = 'running';
  canvas.focus();
  requestAnimationFrame(frame);
}

/** Gives an image (a file's bytes; none for a missing one) to the texture `id`, again when its file changes. */
export function loadImage(id, file) {
  // The view lasts only for this call, so it's copied before decoding (which finishes later).
  const bytes = file.slice();
  const old = textures.get(id);
  if (old && old !== missing)
    gl.deleteTexture(old);
  if (bytes.length === 0) {
    textures.set(id, missing);
    return;
  }

  // Nothing shows until the browser has decoded it.
  const texture = makeTexture(new Uint8Array([0, 0, 0, 0]), 1, 1);
  textures.set(id, texture);
  globalThis.griff.pending++;
  createImageBitmap(new Blob([bytes]), { premultiplyAlpha: 'none', colorSpaceConversion: 'none' })
    .then(image => upload(texture, image))
    .catch(() => {
      if (textures.get(id) === texture)
        textures.set(id, missing);
      gl.deleteTexture(texture);
    })
    .finally(() => globalThis.griff.pending--);
}

/**
 * Copies `rows` rows from `top` of a glyph atlas page (RGBA, `size` pixels wide) to its texture `id`, making it `size`
 * pixels square the first time. Text is smoothed, as on the desktop.
 */
export function glyphs(id, size, top, rows, rgba) {
  let texture = textures.get(id);
  if (!texture) {
    texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, size, size, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
    filter(gl.LINEAR);
    textures.set(id, texture);
  }

  gl.bindTexture(gl.TEXTURE_2D, texture);
  unpack();
  gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, top, size, rows, gl.RGBA, gl.UNSIGNED_BYTE, rgba.slice());
}

/** Draws a frame's draw list into the canvas, `width` by `height` pixels; returns how many draw calls it took. */
export function draw(quads, commandView, valueView, width, height) {
  const length = quads.byteLength;
  if (quadBytes.byteLength < length)
    quadBytes = new Uint8Array(Math.max(length, quadBytes.byteLength * 2));
  quads.copyTo(quadBytes.subarray(0, length));
  const commands = commandView.slice();
  const values = new Float32Array(valueView.slice().buffer);

  let calls = 0;
  let target = null;
  // Clip space covers the whole canvas; a view only clips drawing to its part.
  gl.viewport(0, 0, width, height);
  gl.bindVertexArray(vao);
  gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
  gl.bufferData(gl.ARRAY_BUFFER, quadBytes.subarray(0, length), gl.STREAM_DRAW);
  gl.enable(gl.BLEND);
  gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
  gl.enable(gl.SCISSOR_TEST);
  gl.activeTexture(gl.TEXTURE0);

  // A run of quads, from where they start in the buffer (WebGL 2 has no base instance).
  const quadsFrom = (first, count) => {
    gl.bindVertexArray(vao);
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    for (let attribute = 0; attribute < 5; attribute++)
      gl.vertexAttribPointer(attribute, 4, gl.FLOAT, false, QUAD_BYTES, first * QUAD_BYTES + attribute * 16);
    gl.drawArraysInstanced(gl.TRIANGLE_STRIP, 0, 4, count);
    calls++;
  };
  // A quad over the whole canvas, showing a target's texture the right way up (one drawn into starts at the bottom).
  const wholeView = () => {
    whole.fill(0);
    whole.set([-1, 1, 1, 1, 1, -1, -1, -1, 0, 1, 1, -1, 1, 1, 1, 1]);
    gl.bindVertexArray(wholeVao);
    gl.bindBuffer(gl.ARRAY_BUFFER, wholeBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, whole, gl.STREAM_DRAW);
    gl.drawArraysInstanced(gl.TRIANGLE_STRIP, 0, 4, 1);
    calls++;
  };
  const useShape = mode => {
    gl.useProgram(shapeProgram);
    gl.uniform1i(shapeMode, mode);
  };

  for (let c = 0; c < commands.length; c += COMMAND_INTS) {
    const [op, texture, first, count, at] = [commands[c], commands[c + 1], commands[c + 2], commands[c + 3], commands[c + 4]];
    switch (op) {
      case TARGET:
        if (at === PICTURE) {
          picture = ensureTarget(picture, width, height, false);
          target = picture.framebuffer;
        } else {
          target = null;
        }

        gl.bindFramebuffer(gl.FRAMEBUFFER, target);
        break;
      case VIEW:
        clip(values[at], values[at + 1], values[at + 2], values[at + 3], height);
        break;
      case CLEAR:
        gl.clearColor(values[at], values[at + 1], values[at + 2], 1);
        gl.clear(gl.COLOR_BUFFER_BIT);
        break;
      case SPRITES:
        gl.useProgram(spriteProgram);
        gl.bindTexture(gl.TEXTURE_2D, textureOf(texture));
        quadsFrom(first, count);
        break;
      case HOLES:
        useShape(1);
        quadsFrom(first, count);
        break;
      case BEGIN_LIGHT:
        light = ensureTarget(light, width, height, true);
        gl.bindFramebuffer(gl.FRAMEBUFFER, light.framebuffer);
        gl.clearColor(values[at], values[at + 1], values[at + 2], 1);
        gl.clear(gl.COLOR_BUFFER_BIT);
        gl.blendFunc(gl.ONE, gl.ONE);
        break;
      case LIGHTS:
        useShape(0);
        quadsFrom(first, count);
        break;
      case SHADOWS:
        // The shadows mark the stencil, and the light after is added up only where they didn't.
        gl.enable(gl.STENCIL_TEST);
        gl.clearStencil(0);
        gl.clear(gl.STENCIL_BUFFER_BIT);
        gl.colorMask(false, false, false, false);
        gl.stencilFunc(gl.ALWAYS, 1, 0xFF);
        gl.stencilOp(gl.KEEP, gl.KEEP, gl.REPLACE);
        useShape(0);
        quadsFrom(first, count);
        gl.colorMask(true, true, true, true);
        gl.stencilFunc(gl.EQUAL, 0, 0xFF);
        gl.stencilOp(gl.KEEP, gl.KEEP, gl.KEEP);
        break;
      case SHADOWED_LIGHTS:
        useShape(0);
        quadsFrom(first, count);
        gl.disable(gl.STENCIL_TEST);
        break;
      case END_LIGHT:
        // The light map over the view (drawing is clipped to it), multiplying what's there.
        gl.bindFramebuffer(gl.FRAMEBUFFER, target);
        gl.blendFunc(gl.DST_COLOR, gl.ZERO);
        gl.useProgram(spriteProgram);
        gl.bindTexture(gl.TEXTURE_2D, light.texture);
        wholeView();
        gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
        break;
      case EFFECTS:
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        clip(0, 0, width, height, height);
        gl.disable(gl.BLEND);
        gl.useProgram(effectsProgram);
        gl.uniform2f(effects.uSize, width, height);
        LOOK.forEach((name, i) => gl.uniform1f(effects[name], values[at + i]));
        gl.uniform4f(effects.uTint, values[at + LOOK_TINT], values[at + LOOK_TINT + 1], values[at + LOOK_TINT + 2], 1);
        gl.uniform1f(effects.uGrain, values[at + LOOK_GRAIN]);
        gl.uniform1i(effects.uPalette, values[at + LOOK_PALETTE]);
        gl.uniform1f(effects.uTime, values[at + LOOK_TIME]);
        gl.bindTexture(gl.TEXTURE_2D, picture.texture);
        wholeView();
        gl.enable(gl.BLEND);
        break;
    }
  }

  gl.disable(gl.SCISSOR_TEST);
  gl.bindVertexArray(null);
  if (capturing) {
    const resolve = capturing;
    capturing = null;
    const pixels = read(width, height);
    let text = '';
    for (let i = 0; i < pixels.length; i += 0x8000)
      text += String.fromCharCode.apply(null, pixels.subarray(i, i + 0x8000));
    resolve(btoa(text));
  }

  return calls;
}

/** Reads what was just drawn into `pixels`, as top-down RGBA rows. */
export function capture(pixels, width, height) {
  pixels.set(read(width, height));
}

// The canvas's pixels, top row first (WebGL reads from the bottom).
function read(width, height) {
  gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  const bottomUp = new Uint8Array(width * height * 4);
  gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, bottomUp);
  const topDown = new Uint8Array(bottomUp.length);
  const stride = width * 4;
  for (let y = 0; y < height; y++)
    topDown.set(bottomUp.subarray((height - 1 - y) * stride, (height - y) * stride), y * stride);
  return topDown;
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

function textureOf(id) {
  return textures.get(id) ?? (id > 0 ? missing : white);
}

// Drawing goes only into the view (which WebGL counts from the bottom left).
function clip(left, top, width, height, targetHeight) {
  gl.scissor(left, targetHeight - top - height, Math.max(width, 0), Math.max(height, 0));
}


// A picture the size of the canvas to draw into (the light map, with a stencil buffer for shadows, or the picture the
// screen effects are drawn from), made again when the canvas's size changes.
function ensureTarget(target, width, height, stencil) {
  if (target && target.width === width && target.height === height)
    return target;
  if (target) {
    gl.deleteFramebuffer(target.framebuffer);
    gl.deleteTexture(target.texture);
    if (target.stencil)
      gl.deleteRenderbuffer(target.stencil);
  }

  const made = { width, height, texture: gl.createTexture(), framebuffer: gl.createFramebuffer(), stencil: null };
  gl.bindTexture(gl.TEXTURE_2D, made.texture);
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
  filter(gl.LINEAR);
  gl.bindFramebuffer(gl.FRAMEBUFFER, made.framebuffer);
  gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, made.texture, 0);
  if (stencil) {
    made.stencil = gl.createRenderbuffer();
    gl.bindRenderbuffer(gl.RENDERBUFFER, made.stencil);
    gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH24_STENCIL8, width, height);
    gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_STENCIL_ATTACHMENT, gl.RENDERBUFFER, made.stencil);
  }

  return made;
}

function setUp(header, vertex, sprite, shape, effectsSource) {
  const program = fragment => link(header + vertex, header + fragment);
  spriteProgram = program(sprite);
  gl.useProgram(spriteProgram);
  gl.uniform1i(gl.getUniformLocation(spriteProgram, 'uTexture'), 0);
  shapeProgram = program(shape);
  shapeMode = gl.getUniformLocation(shapeProgram, 'uMode');
  effectsProgram = program(effectsSource);
  gl.useProgram(effectsProgram);
  gl.uniform1i(gl.getUniformLocation(effectsProgram, 'uTexture'), 0);
  effects = {};
  for (const name of [...LOOK, 'uSize', 'uTint', 'uGrain', 'uPalette', 'uTime'])
    effects[name] = gl.getUniformLocation(effectsProgram, name);

  // Every attribute is the instance's: a quad's four corners all read the same one.
  const quadArray = () => {
    const array = gl.createVertexArray();
    gl.bindVertexArray(array);
    const data = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, data);
    for (let attribute = 0; attribute < 5; attribute++) {
      gl.enableVertexAttribArray(attribute);
      gl.vertexAttribPointer(attribute, 4, gl.FLOAT, false, QUAD_BYTES, attribute * 16);
      gl.vertexAttribDivisor(attribute, 1);
    }

    return [array, data];
  };
  [vao, buffer] = quadArray();
  [wholeVao, wholeBuffer] = quadArray();
  gl.bindVertexArray(null);
  white = makeTexture(new Uint8Array([255, 255, 255, 255]), 1, 1);
  missing = makeTexture(new Uint8Array([255, 0, 255, 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 0, 255, 255]), 2, 2);
}

function link(vertexSource, fragmentSource) {
  const compile = (type, source) => {
    const shader = gl.createShader(type);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS))
      throw new Error(`A shader failed to compile: ${gl.getShaderInfoLog(shader)}`);
    return shader;
  };
  const linked = gl.createProgram();
  gl.attachShader(linked, compile(gl.VERTEX_SHADER, vertexSource));
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
  unpack();
  if (source instanceof Uint8Array)
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, source);
  else
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, gl.RGBA, gl.UNSIGNED_BYTE, source);
  filter(gl.NEAREST);
}

function unpack() {
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
  gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
  gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL, gl.NONE);
}

function filter(mode) {
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, mode);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, mode);
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
